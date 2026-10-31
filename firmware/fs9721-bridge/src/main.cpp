// Readout bridge for FS9721 meters.
//
// Reads the meter chip's serial output (2400 baud, 8N1, 14-byte frames of LCD segments) and
// forwards the bytes over Bluetooth LE as they come. The Readout app frames and decodes them,
// so this stays the same for any chip that only sends. The Bluetooth layout matches
// Readout.Core/Protocol/BridgeBluetooth.cs.

#include <Arduino.h>
#include <NimBLEDevice.h>

#ifndef METER_RX_PIN
#define METER_RX_PIN 16
#endif
#ifndef METER_TX_PIN
#define METER_TX_PIN 17  // Unused (the meter only sends), but the UART needs a pin.
#endif
#ifndef METER_RX_INVERTED
#define METER_RX_INVERTED 1
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

static const char *FIRMWARE_VERSION = "0.1.0";
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

  meter.begin(METER_BAUD, SERIAL_8N1, METER_RX_PIN, METER_TX_PIN, METER_RX_INVERTED != 0);
  Serial.printf("Readout bridge %s: %s on GPIO %d, %s\n", FIRMWARE_VERSION, CHIP, METER_RX_PIN,
                METER_RX_INVERTED ? "through an optocoupler (inverted)" : "wired directly");

  startBluetooth();
}

void loop() {
  while (meter.available() > 0) {
    if (pendingLength == MAX_CHUNK) sendPending();
    pending[pendingLength++] = (uint8_t)meter.read();
    lastByteAt = millis();
  }

  if (pendingLength > 0 && millis() - lastByteAt >= FRAME_GAP_MS) sendPending();

  if (ledOffAt != 0 && (int32_t)(millis() - ledOffAt) >= 0) {
    setLed(false);
    ledOffAt = 0;
  }

  delay(1);
}
