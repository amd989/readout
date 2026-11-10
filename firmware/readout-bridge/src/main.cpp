// Readout bridge: an ESP32 wired to a chip's serial port that forwards its bytes over
// Bluetooth LE to the Readout app, which frames and decodes them. The Bluetooth layout matches
// Readout.Core/Protocol/BridgeBluetooth.cs.
//
// The chip is picked at build time (platformio.ini):
//
// - FS9721 (the default): a cheap meter's FS9721_LP3 chip. Receive-only at 2400 baud, 8N1,
//   14-byte frames of LCD segments, usually through an optocoupler. It turns on and off with the
//   meter: when no data has arrived for SLEEP_AFTER_S seconds (the meter is off, or its serial
//   output is), it goes to deep sleep, and the next change on the input line wakes it.
// - DSO150 (CHIP_DSO150): a JYE Tech DSO150 oscilloscope running DSOShell. Two-way at 115200
//   baud, 8N1, wired directly (both sides are 3.3 V). What the app writes to the RX
//   characteristic goes out the UART unchanged. It never sleeps: the scope is quiet until asked.
//
// Either way the bridge doesn't look inside the bytes.

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <esp_sleep.h>
#if CONFIG_IDF_TARGET_ESP32
#include <driver/rtc_io.h>
#endif

#if defined(CHIP_DSO150)
#define CHIP_NAME "DSO150"
#define TWO_WAY 1
#ifndef UART_BAUD
#define UART_BAUD 115200
#endif
#ifndef SLEEP_AFTER_S
#define SLEEP_AFTER_S 0
#endif
// A byte takes 87 µs at 115200 baud, and the UART hands them over in bursts (see setup), so a
// pause this long means the chip has stopped talking for now.
#define FLUSH_GAP_US 3000
#else
#define CHIP_NAME "FS9721"
#define TWO_WAY 0
#ifndef UART_BAUD
#define UART_BAUD 2400
#endif
#ifndef SLEEP_AFTER_S
#define SLEEP_AFTER_S 10  // 0 never sleeps
#endif
// A byte takes about 4 ms at 2400 baud and frames come about 300 ms apart, so a pause this
// long means the frame is done: send what has arrived in one notification.
#define FLUSH_GAP_US 15000
#endif

#ifndef UART_RX_PIN
#define UART_RX_PIN 16
#endif
#ifndef UART_TX_PIN
#define UART_TX_PIN 17  // Unused by receive-only chips, but the UART needs a pin.
#endif
#ifndef UART_RX_INVERTED
#define UART_RX_INVERTED 0
#endif
#ifndef METER_SYMBOLS
#define METER_SYMBOLS "°F,hFE,°C,"
#endif
#ifndef LED_PIN
#define LED_PIN -1
#endif
#ifndef LED_ACTIVE_LOW
#define LED_ACTIVE_LOW 0
#endif

static const char *FIRMWARE_VERSION = "0.3.0";
static const char *CHIP = CHIP_NAME;

// Must match BridgeBluetooth in the app.
static const char *SERVICE_UUID = "1ac73563-3941-40bc-90d4-47fb89f88271";
static const char *DATA_UUID = "4d19a30c-94d7-404e-a69d-b513188f13dc";
static const char *INFO_UUID = "6daa6868-77cd-4539-8b02-6a82b3c6bc0c";
static const char *RX_UUID = "3065c3ca-b78a-4b98-96b6-280cf0784ec4";

// Asked for at connect (BridgeBluetooth.PreferredMtu); a notification carries the negotiated
// MTU less 3 bytes of header: 244 at 247, 20 at the default 23.
static const uint16_t PREFERRED_MTU = 247;
static const uint16_t DEFAULT_MTU = 23;
static const size_t MAX_CHUNK = PREFERRED_MTU - 3;

static HardwareSerial &chip = Serial1;
static NimBLECharacteristic *dataCharacteristic = nullptr;

static uint8_t pending[MAX_CHUNK];
static size_t pendingLength = 0;
static uint32_t lastByteAt = 0;    // millis(), for the sleep timer
static uint32_t lastByteAtUs = 0;  // micros(), for the flush gap
static uint32_t ledOffAt = 0;

// The connected app's MTU. Notifications longer than it allows would be cut short.
static volatile uint16_t peerMtu = DEFAULT_MTU;
static size_t chunkLimit() {
  size_t limit = peerMtu > 3 ? peerMtu - 3 : 20;
  return limit < MAX_CHUNK ? limit : MAX_CHUNK;
}

// What the last notify() reported (it reports through the callback, while it runs).
static volatile int lastNotifyError = 0;
static uint32_t notifyRetries = 0;
static uint32_t bytesDropped = 0;

static void setLed(bool on) {
  if (LED_PIN < 0) return;
  digitalWrite(LED_PIN, (on != (LED_ACTIVE_LOW != 0)) ? HIGH : LOW);
}

static void blink() {
  setLed(true);
  ledOffAt = millis() + 30;
  if (ledOffAt == 0) ledOffAt = 1;
}

// Text as it is, anything else as a dot; long chunks (waveform blocks) only as a byte count, so
// the USB serial port doesn't fall behind the scope.
static void printChunk(const char *direction, const uint8_t *data, size_t length) {
  if (length > 64) {
    Serial.printf("%s [%u bytes]\n", direction, (unsigned)length);
    return;
  }
  char text[70];
  size_t n = 0;
  for (size_t i = 0; i < length; i++) {
    uint8_t c = data[i];
    if (c == '\n' || c == '\r') continue;
    text[n++] = (c >= 0x20 && c < 0x7f) ? (char)c : '.';
  }
  text[n] = 0;
  Serial.printf("%s %s\n", direction, text);
}

#if !TWO_WAY
// When nothing arrives for a while, say what the RX pin is doing, to help with the wiring.
static const uint32_t QUIET_REPORT_MS = 5000;
static uint32_t quietSince = 0;
static uint32_t samplesHigh = 0;
static uint32_t samplesLow = 0;

static void reportQuiet() {
  uint32_t total = samplesHigh + samplesLow;
  const char *level = samplesLow == 0 ? "always high: nothing is arriving (is RS232 on the meter's LCD? is the LED wired right?)"
                      : samplesHigh == 0 ? "always low: check the wires, or flash the build for your wiring (with or without -vdd)"
                                         : "changing, but no bytes: flash the build for your wiring (with or without -vdd)";
  Serial.printf("no data for %lu s; GPIO %d is %s (%lu%% high)\n", (unsigned long)(QUIET_REPORT_MS / 1000),
                UART_RX_PIN, level, (unsigned long)(total ? samplesHigh * 100 / total : 0));

  // How far the line actually swings, measured over half a second (frames come about 3 a
  // second): an optocoupler that gets some light but too little pulls it only partway down.
  // The ESP32 reads below about 0.8 V as low.
  uint32_t lowest = UINT32_MAX, highest = 0;
  uint32_t until = millis() + 500;
  while ((int32_t)(millis() - until) < 0) {
    uint32_t mv = analogReadMilliVolts(UART_RX_PIN);
    if (mv < lowest) lowest = mv;
    if (mv > highest) highest = mv;
  }
  Serial.printf("  GPIO %d swings %lu–%lu mV%s\n", UART_RX_PIN, (unsigned long)lowest, (unsigned long)highest,
                lowest > 800 && highest - lowest > 200 ? ": the optocoupler dips but not to a low; try a larger pull-up or more LED current" : "");
  // Measuring switched the pin to analog (it reads as low then) and took it from the UART;
  // make it a digital input again and give it back.
  pinMode(UART_RX_PIN, INPUT);
  chip.begin(UART_BAUD, SERIAL_8N1, UART_RX_PIN, UART_TX_PIN, UART_RX_INVERTED != 0);
}
#endif

static const char *wakeReason() {
  switch (esp_sleep_get_wakeup_cause()) {
    case ESP_SLEEP_WAKEUP_EXT0:
    case ESP_SLEEP_WAKEUP_GPIO:
      return "woken by the meter";
    case ESP_SLEEP_WAKEUP_UNDEFINED:
      return "powered on";
    default:
      return "woken";
  }
}

// Deep sleep until the input line changes. Usually it rests high (the meter idles high, or is
// off with the optocoupler's pull-up), and the next start bit wakes it. Wired directly, a meter
// that's off holds the line low instead, so then it waits for the line to go high (the meter
// switching on). Either way it wakes on whatever comes next.
static void sleepUntilTheMeterTalks() {
  int level = digitalRead(UART_RX_PIN);
  Serial.printf("no data for %d s: sleeping until GPIO %d goes %s\n", SLEEP_AFTER_S, UART_RX_PIN,
                level ? "low" : "high");
  Serial.flush();
  setLed(false);
  chip.end();
  // A weak pull toward the level it rests at, so a line nothing drives (a loose wire, a meter
  // that lets go of TXD) doesn't wake it on noise. The optocoupler's 10 kΩ pull-up wins anyway.
  gpio_num_t pin = (gpio_num_t)UART_RX_PIN;
#if CONFIG_IDF_TARGET_ESP32
  rtc_gpio_init(pin);
  rtc_gpio_set_direction(pin, RTC_GPIO_MODE_INPUT_ONLY);
  if (level) { rtc_gpio_pulldown_dis(pin); rtc_gpio_pullup_en(pin); }
  else { rtc_gpio_pullup_dis(pin); rtc_gpio_pulldown_en(pin); }
  esp_sleep_enable_ext0_wakeup(pin, level ? 0 : 1);
#else
  gpio_set_pull_mode(pin, level ? GPIO_PULLUP_ONLY : GPIO_PULLDOWN_ONLY);
  esp_deep_sleep_enable_gpio_wakeup(1ULL << UART_RX_PIN,
                                    level ? ESP_GPIO_WAKEUP_GPIO_LOW : ESP_GPIO_WAKEUP_GPIO_HIGH);
#endif
  esp_deep_sleep_start();
}

class ServerCallbacks : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer *server, ble_gap_conn_desc *desc) override {
    peerMtu = DEFAULT_MTU;
    Serial.printf("app connected (%d)\n", server->getConnectedCount());
#if TWO_WAY
    // Waveforms are about 1.1 KB: ask for a short connection interval (15–30 ms, in 1.25 ms
    // units) so several notifications go out every few milliseconds. Timeout 4 s (10 ms units).
    server->updateConnParams(desc->conn_handle, 12, 24, 0, 400);
#endif
  }
  void onDisconnect(NimBLEServer *server) override {
    // NimBLE starts advertising again by itself.
    peerMtu = DEFAULT_MTU;
    Serial.println("app disconnected");
  }
  void onMTUChange(uint16_t mtu, ble_gap_conn_desc *desc) override {
    peerMtu = mtu;
    Serial.printf("mtu %u: notifications of up to %u bytes\n", mtu, (unsigned)chunkLimit());
  }
};

class DataCallbacks : public NimBLECharacteristicCallbacks {
  void onStatus(NimBLECharacteristic *characteristic, Status status, int code) override {
    lastNotifyError = status == SUCCESS_NOTIFY ? 0 : (code != 0 ? code : -1);
  }
};

#if TWO_WAY
// The app's bytes for the chip: out the UART as they are.
class RxCallbacks : public NimBLECharacteristicCallbacks {
  void onWrite(NimBLECharacteristic *characteristic) override {
    NimBLEAttValue value = characteristic->getValue();
    if (value.length() == 0) return;
    chip.write(value.data(), value.length());
    printChunk("<-", value.data(), value.length());
    blink();
  }
};
#endif

static String deviceName() {
  // The last two bytes of the chip's MAC, so several bridges can be told apart.
  uint64_t mac = ESP.getEfuseMac();
  char name[32];
  snprintf(name, sizeof name, "Readout %s %02X%02X", CHIP,
           (unsigned)((mac >> 32) & 0xFF), (unsigned)((mac >> 40) & 0xFF));
  return String(name);
}

static void startBluetooth() {
  String name = deviceName();
  NimBLEDevice::init(name.c_str());
  NimBLEDevice::setPower(ESP_PWR_LVL_P9);
  NimBLEDevice::setMTU(PREFERRED_MTU);

  NimBLEServer *server = NimBLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  NimBLEService *service = server->createService(SERVICE_UUID);
  dataCharacteristic = service->createCharacteristic(DATA_UUID, NIMBLE_PROPERTY::NOTIFY);
  dataCharacteristic->setCallbacks(new DataCallbacks());

  NimBLECharacteristic *info = service->createCharacteristic(INFO_UUID, NIMBLE_PROPERTY::READ);
  String text = String("chip=") + CHIP + "\nbaud=" + UART_BAUD + "\nfirmware=" + FIRMWARE_VERSION;
#if !TWO_WAY
  text += String("\nsymbols=") + METER_SYMBOLS;
#endif
  info->setValue(text.c_str());

#if TWO_WAY
  NimBLECharacteristic *rx =
      service->createCharacteristic(RX_UUID, NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR);
  rx->setCallbacks(new RxCallbacks());
#endif

  service->start();

  // The name goes in the advertisement itself: apps find bridges by it, and some scanners
  // (BlueZ on Linux) don't always pick up a scan response. The 128-bit service UUID doesn't fit
  // beside it, so it goes in the scan response. "BR/EDR not supported" tells BlueZ to use LE.
  NimBLEAdvertisementData advertisement;
  advertisement.setFlags(BLE_HS_ADV_F_DISC_GEN | BLE_HS_ADV_F_BREDR_UNSUP);
  advertisement.setName(name.c_str());
  NimBLEAdvertisementData scanResponse;
  scanResponse.setCompleteServices(NimBLEUUID(SERVICE_UUID));

  NimBLEAdvertising *advertising = NimBLEDevice::getAdvertising();
  advertising->setAdvertisementData(advertisement);
  advertising->setScanResponseData(scanResponse);
  advertising->start();

  Serial.printf("advertising as \"%s\"\n", name.c_str());
}

// One notification. When the Bluetooth stack's buffers are full (BLE_HS_ENOMEM: notifications
// queued faster than the link sends them), wait a little and try again rather than lose bytes;
// the UART's buffer holds what arrives meanwhile.
static void notify(const uint8_t *data, size_t length) {
  for (int attempt = 0; attempt < 50; attempt++) {
    lastNotifyError = 0;
    dataCharacteristic->setValue(data, length);
    dataCharacteristic->notify();
    if (lastNotifyError != BLE_HS_ENOMEM) return;
    notifyRetries++;
    delay(2);
  }
  bytesDropped += length;
  Serial.printf("dropped %u bytes: the link isn't keeping up (%lu dropped so far)\n", (unsigned)length,
                (unsigned long)bytesDropped);
}

static void sendPending() {
  if (pendingLength == 0) return;

#if TWO_WAY
  printChunk("->", pending, pendingLength);
#else
  if (Serial) {
    for (size_t i = 0; i < pendingLength; i++)
      Serial.printf("%02x ", pending[i]);
    Serial.println();
  }
#endif

  NimBLEServer *server = NimBLEDevice::getServer();
  if (server != nullptr && server->getConnectedCount() > 0) notify(pending, pendingLength);

  blink();
  pendingLength = 0;
}

void setup() {
  Serial.begin(115200);
  if (LED_PIN >= 0) pinMode(LED_PIN, OUTPUT);
  setLed(false);

#if CONFIG_IDF_TARGET_ESP32
  // A wake-up from deep sleep left the pin as an RTC pad; hand it back to the UART.
  rtc_gpio_deinit((gpio_num_t)UART_RX_PIN);
#endif
#if TWO_WAY
  // Room for several waveforms while a notification waits for the link (1.1 KB each).
  chip.setRxBufferSize(4096);
#endif
  chip.begin(UART_BAUD, SERIAL_8N1, UART_RX_PIN, UART_TX_PIN, UART_RX_INVERTED != 0);
#if TWO_WAY
  // The UART passes bytes on when 120 have queued in its FIFO, or after a pause: 10 ms bursts
  // at 115200. Smaller bursts keep the flush gap short and notifications evenly filled.
  chip.setRxFIFOFull(16);
#endif
#if TWO_WAY
  Serial.printf("Readout bridge %s (%s): %s at %d baud, RX on GPIO %d, TX on GPIO %d\n", FIRMWARE_VERSION,
                wakeReason(), CHIP, UART_BAUD, UART_RX_PIN, UART_TX_PIN);
#else
  Serial.printf("Readout bridge %s (%s): %s on GPIO %d%s\n", FIRMWARE_VERSION, wakeReason(), CHIP, UART_RX_PIN,
                UART_RX_INVERTED ? ", inverted" : "");
#endif
  lastByteAt = millis();
  lastByteAtUs = micros();

  startBluetooth();
}

void loop() {
  bool received = false;
  size_t limit = chunkLimit();
  while (chip.available() > 0) {
    if (pendingLength >= limit) sendPending();
    pending[pendingLength++] = (uint8_t)chip.read();
    received = true;
  }
  if (received) {
    lastByteAt = millis();
    lastByteAtUs = micros();
  }
  if (pendingLength >= limit) sendPending();

#if !TWO_WAY
  // The line as the UART sees it: idle is high once the inversion is right.
  bool high = (digitalRead(UART_RX_PIN) == HIGH) != (UART_RX_INVERTED != 0);
  (high ? samplesHigh : samplesLow)++;
  if (received) {
    quietSince = millis();
    samplesHigh = samplesLow = 0;
  } else if (millis() - quietSince >= QUIET_REPORT_MS) {
    reportQuiet();
    quietSince = millis();
    samplesHigh = samplesLow = 0;
  }
#endif

  if (pendingLength > 0 && micros() - lastByteAtUs >= FLUSH_GAP_US) sendPending();

  if (SLEEP_AFTER_S > 0 && millis() - lastByteAt >= SLEEP_AFTER_S * 1000UL) sleepUntilTheMeterTalks();

  if (ledOffAt != 0 && (int32_t)(millis() - ledOffAt) >= 0) {
    setLed(false);
    ledOffAt = 0;
  }

  // Busy while bytes are coming; otherwise rest a little.
  if (!received) delay(1);
}
