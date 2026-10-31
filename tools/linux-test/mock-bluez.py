#!/usr/bin/env python3
"""A stand-in for BlueZ on the system bus, to exercise Readout's Linux connection without one.

Docker on a Mac has no Bluetooth controller, so this plays bluetoothd for one adapter and two
remote devices, and logs what the app does (one "[mock]" line per event on stdout):

- A meter, "121GW" (dev_88_6B_0F_12_34_56). While the app discovers, its RSSI changes every
  second. Connect resolves a GAP service and the meter's serial service with the data
  characteristic (indicate, write). After StartNotify it indicates a packet every 250 ms:
  DC volts around 12.3 V, split into chunks the way the meter's radio splits them (a packet
  across two indications, or the end of one with the start of the next). Writes are logged as
  key presses; MODE steps through DC V, AC V, ohms and continuity, HOLD holds the value.
- A speaker, "Kitchen" (dev_11_22_33_44_55_66), which advertises too and must not be listed.

Test hooks, on the mock's own interface (dbus-send --system --dest=org.bluez / ...):
  org.readout.Mock.Drop      the meter goes out of range (the link drops) and comes back after 8 s
  org.readout.Mock.Power b   the adapter is powered on or off

Run on a bus whose policy lets it own org.bluez (system.conf here).
"""
import math
import sys

import dbus
import dbus.mainloop.glib
import dbus.service
from gi.repository import GLib

DATA = "e7add780-b042-4876-aae1-112855353cc1"
SERIAL_SERVICE = "0bd51666-e7cb-469b-8e4d-2742f1ba77cc"
GAP = "00001800-0000-1000-8000-00805f9b34fb"
DEVICE_NAME = "00002a00-0000-1000-8000-00805f9b34fb"

OM = "org.freedesktop.DBus.ObjectManager"
PROPS = "org.freedesktop.DBus.Properties"
ADAPTER_IF = "org.bluez.Adapter1"
DEVICE_IF = "org.bluez.Device1"
SERVICE_IF = "org.bluez.GattService1"
CHAR_IF = "org.bluez.GattCharacteristic1"
MOCK_IF = "org.readout.Mock"

ADAPTER = "/org/bluez/hci0"
METER = ADAPTER + "/dev_88_6B_0F_12_34_56"
SPEAKER = ADAPTER + "/dev_11_22_33_44_55_66"

KEYS = {1: "RANGE", 2: "HOLD", 3: "REL", 4: "PEAK", 5: "MODE", 6: "MIN/MAX", 7: "MEM", 8: "SETUP"}
# (mode on the wire, range index, coupling, DC icon, AC icon)
MODES = [(1, 1, 1, True, False), (2, 1, 2, False, True), (9, 2, 0, False, False), (10, 0, 0, False, False)]


def log(*parts):
    print("[mock]", *parts, flush=True)


def packet(mode, range_index, raw, coupling, dc, ac, hold):
    """A 19-byte packet, as Readout.Core's PacketBuilder makes them (docs/PROTOCOL.md)."""
    d = bytearray(19)
    d[0] = 0xF2
    d[1] = 17                     # year 2017
    d[2] = 10 << 4 | 1            # October, serial 12345
    d[3] = 0x23
    d[4] = 0x45
    d[5] = (mode & 0x1F) | ((raw >> 16) & 0x3) << 6
    d[6] = range_index & 0x0F
    d[7] = (raw >> 8) & 0xFF
    d[8] = raw & 0xFF
    d[9] = 110                    # sub display: battery
    d[10] = 2                     # two decimals
    d[11], d[12] = (812 >> 8), 812 & 0xFF   # 8.12 V
    d[13] = 1                     # bar on, full scale 50
    d[14] = min(31, raw * 26 // 50000)
    d[15] = (coupling & 0x3) << 3 | 0x04 | 0x02   # AUTO, APO
    d[16] = 0x40                  # Bluetooth
    d[17] = (2 << 2 if hold else 0) | (0x02 if ac else 0) | (0x01 if dc else 0)
    x = 0
    for b in d[:18]:
        x ^= b
    d[18] = x
    return bytes(d)


def byte_array(data):
    return dbus.Array([dbus.Byte(b) for b in data], signature="y")


class Obj(dbus.service.Object):
    """An object with properties per interface, a Properties interface and PropertiesChanged."""

    def __init__(self, bus, path, interfaces):
        super().__init__(bus, path)
        self.path = path
        self.interfaces = interfaces  # name -> dict of properties

    @dbus.service.method(PROPS, in_signature="ss", out_signature="v")
    def Get(self, interface, name):
        return self.interfaces[interface][name]

    @dbus.service.method(PROPS, in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        return self.interfaces.get(interface, {})

    # Like bluetoothd without -E: PreferredBearer (an experimental property) isn't there.
    @dbus.service.method(PROPS, in_signature="ssv")
    def Set(self, interface, name, value):
        if name not in self.interfaces.get(interface, {}):
            log("app sets", name, "=", str(value), "(refused: no such property)")
            raise dbus.exceptions.DBusException("No such property '%s'" % name,
                                                name="org.freedesktop.DBus.Error.InvalidArgs")
        self.set(interface, **{name: value})

    @dbus.service.signal(PROPS, signature="sa{sv}as")
    def PropertiesChanged(self, interface, changed, invalidated):
        pass

    def set(self, interface, **changed):
        self.interfaces[interface].update(changed)
        self.PropertiesChanged(interface, changed, dbus.Array([], signature="s"))


class Root(dbus.service.Object):
    def __init__(self, bus):
        super().__init__(bus, "/")
        self.bus = bus
        self.objects = {}
        self.adapter = None
        self.meter = None

    def add(self, obj, announce=True):
        self.objects[obj.path] = obj
        if announce:
            self.InterfacesAdded(obj.path, obj.interfaces)

    def remove(self, path):
        obj = self.objects.pop(path)
        obj.remove_from_connection()
        self.InterfacesRemoved(path, dbus.Array(list(obj.interfaces), signature="s"))

    @dbus.service.method(OM, out_signature="a{oa{sa{sv}}}")
    def GetManagedObjects(self):
        return {path: obj.interfaces for path, obj in self.objects.items()}

    @dbus.service.signal(OM, signature="oa{sa{sv}}")
    def InterfacesAdded(self, path, interfaces):
        pass

    @dbus.service.signal(OM, signature="oas")
    def InterfacesRemoved(self, path, interfaces):
        pass

    @dbus.service.method(MOCK_IF)
    def Drop(self):
        log("test: the meter goes out of range")
        self.meter.drop()
        self.meter.away = True

        def back():
            log("test: the meter is back in range")
            self.meter.away = False
            return False
        GLib.timeout_add(8000, back)

    @dbus.service.method(MOCK_IF, in_signature="b")
    def Power(self, on):
        log("test: adapter powered", bool(on))
        self.adapter.set(ADAPTER_IF, Powered=dbus.Boolean(on))
        if not on:
            self.meter.drop()


class Adapter(Obj):
    def __init__(self, bus, devices):
        super().__init__(bus, ADAPTER, {
            ADAPTER_IF: {"Address": "00:1A:7D:DA:71:13", "Powered": True, "Discovering": False},
        })
        self.devices = devices
        self.discovery = None
        self.tick = 0

    @dbus.service.method(ADAPTER_IF, in_signature="a{sv}")
    def SetDiscoveryFilter(self, options):
        log("discovery filter", {str(k): str(v) for k, v in options.items()})

    @dbus.service.method(ADAPTER_IF)
    def StartDiscovery(self):
        log("discovery started")
        self.set(ADAPTER_IF, Discovering=True)
        if self.discovery is None:
            self.discovery = GLib.timeout_add(1000, self.advertise)

    @dbus.service.method(ADAPTER_IF)
    def StopDiscovery(self):
        log("discovery stopped")
        # As on the Fedora laptop: the scan is still being taken down for a moment after
        # StopDiscovery returns, and a connect meanwhile is aborted (see Meter.Connect).
        def down():
            self.set(ADAPTER_IF, Discovering=False)
            return False
        GLib.timeout_add(300, down)
        if self.discovery is not None:
            GLib.source_remove(self.discovery)
            self.discovery = None
        # As bluetoothd does: what was heard during discovery is forgotten when it ends.
        for device, _ in self.devices:
            if device.interfaces[DEVICE_IF].pop("RSSI", None) is not None:
                device.PropertiesChanged(DEVICE_IF, {}, dbus.Array(["RSSI"], signature="s"))

    def advertise(self):
        self.tick += 1
        for device, rssi in self.devices:
            if not device.interfaces[DEVICE_IF]["Connected"] and not getattr(device, "away", False):
                device.set(DEVICE_IF, RSSI=dbus.Int16(rssi + (self.tick % 3)))
        return True


class Characteristic(Obj):
    def __init__(self, bus, path, service, uuid, flags, on_write=None, on_notify=None, value=b""):
        super().__init__(bus, path, {CHAR_IF: {
            "UUID": uuid, "Service": dbus.ObjectPath(service),
            "Flags": dbus.Array(flags, signature="s"), "Notifying": False, "Value": byte_array(value),
        }})
        self.on_write, self.on_notify = on_write, on_notify

    @dbus.service.method(CHAR_IF, in_signature="a{sv}", out_signature="ay")
    def ReadValue(self, options):
        return self.interfaces[CHAR_IF]["Value"]

    @dbus.service.method(CHAR_IF, in_signature="aya{sv}")
    def WriteValue(self, value, options):
        self.on_write(bytes(value), str(options.get("type", "")))

    @dbus.service.method(CHAR_IF)
    def StartNotify(self):
        log("app subscribes to", self.interfaces[CHAR_IF]["UUID"])
        self.set(CHAR_IF, Notifying=True)
        if self.on_notify:
            self.on_notify(True)

    @dbus.service.method(CHAR_IF)
    def StopNotify(self):
        log("app unsubscribes")
        self.set(CHAR_IF, Notifying=False)
        if self.on_notify:
            self.on_notify(False)


class Device(Obj):
    def __init__(self, bus, root, path, address, name, flags):
        super().__init__(bus, path, {DEVICE_IF: {
            "Address": address, "AddressType": "public", "Name": name, "Alias": name,
            "AdvertisingFlags": byte_array([flags]), "Connected": False, "ServicesResolved": False,
        }})
        self.bus = bus
        self.root = root

    @dbus.service.method(DEVICE_IF)
    def Connect(self):
        log("app connects to", self.interfaces[DEVICE_IF]["Name"])
        raise dbus.exceptions.DBusException("Not a meter", name="org.bluez.Error.Failed")


class Meter(Device):
    def __init__(self, bus, root):
        # A Bluegiga module: LE General Discoverable, BR/EDR not supported.
        super().__init__(bus, root, METER, "88:6B:0F:12:34:56", "121GW", 0x06)
        self.timer = None
        self.data = None
        self.count = 0
        self.mode = 0
        self.hold = False
        self.pending = b""
        self.away = False

    @dbus.service.method(DEVICE_IF)
    def Connect(self):
        if self.root.adapter.interfaces[ADAPTER_IF]["Discovering"]:
            log("app connects to the meter while the adapter is still scanning: aborted")
            raise dbus.exceptions.DBusException("le-connection-abort-by-local", name="org.bluez.Error.Failed")
        if self.away:
            log("app connects to the meter, which is out of range")
            raise dbus.exceptions.DBusException("le-connection-abort-by-local", name="org.bluez.Error.Failed")
        log("app connects to the meter")
        gap = METER + "/service0001"
        self.root.add(Obj(self.bus, gap, {SERVICE_IF: {"UUID": GAP, "Device": dbus.ObjectPath(METER), "Primary": True}}))
        self.root.add(Characteristic(self.bus, gap + "/char0002", gap, DEVICE_NAME, ["read"], value=b"121GW"))
        serial = METER + "/service0010"
        self.root.add(Obj(self.bus, serial, {SERVICE_IF: {"UUID": SERIAL_SERVICE, "Device": dbus.ObjectPath(METER), "Primary": True}}))
        self.data = Characteristic(self.bus, serial + "/char0011", serial, DATA, ["indicate", "write", "write-without-response"],
                                   on_write=self.key, on_notify=self.subscribed)
        self.root.add(self.data)
        self.set(DEVICE_IF, Connected=True)
        self.set(DEVICE_IF, ServicesResolved=True)

    @dbus.service.method(DEVICE_IF)
    def Disconnect(self):
        log("app disconnects from the meter")
        self.drop()

    def drop(self):
        self.subscribed(False)
        if self.interfaces[DEVICE_IF]["Connected"]:
            self.set(DEVICE_IF, Connected=False, ServicesResolved=False)
        for path in [p for p in self.root.objects if p.startswith(METER + "/")]:
            self.root.remove(path)
        self.data = None

    def subscribed(self, on):
        if on and self.timer is None:
            self.timer = GLib.timeout_add(250, self.send)
        elif not on and self.timer is not None:
            GLib.source_remove(self.timer)
            self.timer = None

    def send(self):
        if self.data is None:
            return False
        self.count += 1
        mode, range_index, coupling, dc, ac = MODES[self.mode]
        if not self.hold:
            self.raw = 12300 + int(200 * math.sin(self.count / 8))
        self.pending += packet(mode, range_index, self.raw, coupling, dc, ac, self.hold)
        # Indications of up to 20 bytes: 19-byte packets straddle them.
        while len(self.pending) >= 20 or (self.pending and self.count % 2 == 0):
            chunk, self.pending = self.pending[:20], self.pending[20:]
            self.data.set(CHAR_IF, Value=byte_array(chunk))
        if self.count % 40 == 0:
            log("indicated", self.count, "packets")
        return True

    def key(self, value, write_type):
        if len(value) == 5 and value[0] == 0xF4:
            key = KEYS.get(value[2] - 0x30, "?")
            long = value[1] == 0x38
            log(f"key {key}{' (long)' if long else ''}, write type {write_type}")
            if key == "MODE":
                self.mode = (self.mode + 1) % len(MODES)
            elif key == "HOLD":
                self.hold = not self.hold
        else:
            log("unexpected write", value.hex())


def main():
    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    bus = dbus.SystemBus()
    name = dbus.service.BusName("org.bluez", bus)
    root = Root(bus)
    meter = Meter(bus, root)
    speaker = Device(bus, root, SPEAKER, "11:22:33:44:55:66", "Kitchen", 0x1A)
    adapter = Adapter(bus, [(meter, -58), (speaker, -70)])
    root.adapter, root.meter = adapter, meter
    for obj in (adapter, meter, speaker):
        root.add(obj, announce=False)
    log("org.bluez ready", name.get_name())
    GLib.MainLoop().run()


if __name__ == "__main__":
    sys.exit(main())
