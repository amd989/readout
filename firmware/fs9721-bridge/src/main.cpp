// Readout bridge for FS9721 meters.
//
// Reads the meter chip's serial output (2400 baud, 8N1, 14-byte frames of LCD segments) and
// forwards the bytes over Bluetooth LE as they come. The Readout app frames and decodes them,
// so this stays the same for any chip that only sends. The Bluetooth layout matches
// Readout.Core/Protocol/BridgeBluetooth.cs.
//
// It turns on and off with the meter: when no data has arrived for SLEEP_AFTER_S seconds (the
// meter is off, or its serial output is), it goes to deep sleep, and the next change on the
// input line wakes it.

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <esp_sleep.h>
#if CONFIG_IDF_TARGET_ESP32
#include <driver/rtc_io.h>
#endif

#ifndef METER_RX_PIN
#define METER_RX_PIN 16
#endif
#ifndef METER_TX_PIN
#define METER_TX_PIN 17  // Unused (the meter only sends), but the UART needs a pin.
#endif
#ifndef METER_RX_INVERTED
#define METER_RX_INVERTED 0
#endif
#ifndef SLEEP_AFTER_S
#define SLEEP_AFTER_S 10  // 0 never sleeps
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

static const char *FIRMWARE_VERSION = "0.2.0";
static const char *CHIP = "FS9721";

// Must match BridgeBluetooth in the app.
static const char *SERVICE_UUID = "1ac73563-3941-40bc-90d4-47fb89f88271";
static const char *DATA_UUID = "4d19a30c-94d7-404e-a69d-b513188f13dc";
static const char *INFO_UUID = "6daa6868-77cd-4539-8b02-6a82b3c6bc0c";

static const uint32_t METER_BAUD = 2400;
// A byte takes about 4 ms at 2400 baud and frames come about 300 ms apart, so a pause this
// long means the frame is done: send what has arrived in one notification.
static const uint32_t FRAME_GAP_MS = 15;
// Default ATT payload is 20 bytes; a frame is 14.
static const size_t MAX_CHUNK = 20;

static HardwareSerial &meter = Serial1;
static NimBLECharacteristic *dataCharacteristic = nullptr;

static uint8_t pending[MAX_CHUNK];
static size_t pendingLength = 0;
static uint32_t lastByteAt = 0;
static uint32_t framesSent = 0;
static uint32_t ledOffAt = 0;

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
                METER_RX_PIN, level, (unsigned long)(total ? samplesHigh * 100 / total : 0));

  // How far the line actually swings, measured over half a second (frames come about 3 a
  // second): an optocoupler that gets some light but too little pulls it only partway down.
  // The ESP32 reads below about 0.8 V as low.
  uint32_t lowest = UINT32_MAX, highest = 0;
  uint32_t until = millis() + 500;
  while ((int32_t)(millis() - until) < 0) {
    uint32_t mv = analogReadMilliVolts(METER_RX_PIN);
    if (mv < lowest) lowest = mv;
    if (mv > highest) highest = mv;
  }
  Serial.printf("  GPIO %d swings %lu–%lu mV%s\n", METER_RX_PIN, (unsigned long)lowest, (unsigned long)highest,
                lowest > 800 && highest - lowest > 200 ? ": the optocoupler dips but not to a low; try a larger pull-up or more LED current" : "");
  // Measuring switched the pin to analog (it reads as low then) and took it from the UART;
  // make it a digital input again and give it back.
  pinMode(METER_RX_PIN, INPUT);
  meter.begin(METER_BAUD, SERIAL_8N1, METER_RX_PIN, METER_TX_PIN, METER_RX_INVERTED != 0);
}

static void setLed(bool on);

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
  int level = digitalRead(METER_RX_PIN);
  Serial.printf("no data for %d s: sleeping until GPIO %d goes %s\n", SLEEP_AFTER_S, METER_RX_PIN,
                level ? "low" : "high");
  Serial.flush();
  setLed(false);
  meter.end();
  // A weak pull toward the level it rests at, so a line nothing drives (a loose wire, a meter
  // that lets go of TXD) doesn't wake it on noise. The optocoupler's 10 kΩ pull-up wins anyway.
  gpio_num_t pin = (gpio_num_t)METER_RX_PIN;
#if CONFIG_IDF_TARGET_ESP32
  rtc_gpio_init(pin);
  rtc_gpio_set_direction(pin, RTC_GPIO_MODE_INPUT_ONLY);
  if (level) { rtc_gpio_pulldown_dis(pin); rtc_gpio_pullup_en(pin); }
  else { rtc_gpio_pullup_dis(pin); rtc_gpio_pulldown_en(pin); }
  esp_sleep_enable_ext0_wakeup(pin, level ? 0 : 1);
#else
  gpio_set_pull_mode(pin, level ? GPIO_PULLUP_ONLY : GPIO_PULLDOWN_ONLY);
  esp_deep_sleep_enable_gpio_wakeup(1ULL << METER_RX_PIN,
                                    level ? ESP_GPIO_WAKEUP_GPIO_LOW : ESP_GPIO_WAKEUP_GPIO_HIGH);
#endif
  esp_deep_sleep_start();
}

static void setLed(bool on) {
  if (LED_PIN < 0) return;
  digitalWrite(LED_PIN, (on != (LED_ACTIVE_LOW != 0)) ? HIGH : LOW);
}

class ServerCallbacks : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer *server) override {
    Serial.printf("app connected (%d)\n", server->getConnectedCount());
  }
  void onDisconnect(NimBLEServer *server) override {
    // NimBLE starts advertising again by itself.
    Serial.println("app disconnected");
  }
};

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

  NimBLEServer *server = NimBLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  NimBLEService *service = server->createService(SERVICE_UUID);
  dataCharacteristic = service->createCharacteristic(DATA_UUID, NIMBLE_PROPERTY::NOTIFY);

  NimBLECharacteristic *info = service->createCharacteristic(INFO_UUID, NIMBLE_PROPERTY::READ);
  String text = String("chip=") + CHIP + "\nsymbols=" + METER_SYMBOLS + "\nfirmware=" + FIRMWARE_VERSION;
  info->setValue(text.c_str());

  service->start();

  NimBLEAdvertising *advertising = NimBLEDevice::getAdvertising();
  advertising->addServiceUUID(SERVICE_UUID);
  // The 128-bit UUID fills most of the advertisement; the name goes in the scan response.
  advertising->setScanResponse(true);
  advertising->start();

  Serial.printf("advertising as \"%s\"\n", name.c_str());
}

static void sendPending() {
  if (pendingLength == 0) return;

  if (Serial) {
    for (size_t i = 0; i < pendingLength; i++)
      Serial.printf("%02x ", pending[i]);
    Serial.println();
  }

  NimBLEServer *server = NimBLEDevice::getServer();
  if (server != nullptr && server->getConnectedCount() > 0) {
    dataCharacteristic->setValue(pending, pendingLength);
    dataCharacteristic->notify();
    framesSent++;
  }

  setLed(true);
  ledOffAt = millis() + 30;
  pendingLength = 0;
}

void setup() {
  Serial.begin(115200);
  if (LED_PIN >= 0) pinMode(LED_PIN, OUTPUT);
  setLed(false);

#if CONFIG_IDF_TARGET_ESP32
  // The wake-up left the pin as an RTC pad; hand it back to the UART.
  rtc_gpio_deinit((gpio_num_t)METER_RX_PIN);
#endif
  meter.begin(METER_BAUD, SERIAL_8N1, METER_RX_PIN, METER_TX_PIN, METER_RX_INVERTED != 0);
  Serial.printf("Readout bridge %s (%s): %s on GPIO %d%s\n", FIRMWARE_VERSION, wakeReason(), CHIP,
                METER_RX_PIN, METER_RX_INVERTED ? ", inverted" : "");
  lastByteAt = millis();

  startBluetooth();
}

void loop() {
  bool received = false;
  while (meter.available() > 0) {
    if (pendingLength == MAX_CHUNK) sendPending();
    pending[pendingLength++] = (uint8_t)meter.read();
    lastByteAt = millis();
    received = true;
  }

  // The line as the UART sees it: idle is high once the inversion is right.
  bool high = (digitalRead(METER_RX_PIN) == HIGH) != (METER_RX_INVERTED != 0);
  (high ? samplesHigh : samplesLow)++;
  if (received) {
    quietSince = millis();
    samplesHigh = samplesLow = 0;
  } else if (millis() - quietSince >= QUIET_REPORT_MS) {
    reportQuiet();
    quietSince = millis();
    samplesHigh = samplesLow = 0;
  }

  if (pendingLength > 0 && millis() - lastByteAt >= FRAME_GAP_MS) sendPending();

  if (SLEEP_AFTER_S > 0 && millis() - lastByteAt >= SLEEP_AFTER_S * 1000UL) sleepUntilTheMeterTalks();

  if (ledOffAt != 0 && (int32_t)(millis() - ledOffAt) >= 0) {
    setLed(false);
    ledOffAt = 0;
  }

  delay(1);
}
