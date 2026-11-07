#!/bin/bash
# Container entry point: a virtual display, then the app from /app with its output in /out.
#   fake   the simulated meter (READOUT_FAKE_BLE=1)
#   mock   the BlueZ connection, against mock-bluez.py on a private system bus (its log: /out/mock.log)
#   none   the BlueZ connection with no system bus at all: the app should say there's no Bluetooth
# "Try the demo meter" works in every mode.
set -e
mode=${1:-fake}
mkdir -p /out
# SCREEN sets the virtual display (e.g. 1280x800x24 for a small laptop).
Xvfb :99 -screen 0 ${SCREEN:-1200x1100x24} &
export DISPLAY=:99 GSK_RENDERER=cairo GTK_A11Y=none
sleep 1
if [ "$mode" = mock ]; then
    mkdir -p /run/dbus
    dbus-daemon --config-file=/linux-test/system.conf --fork
    python3 -u /linux-test/mock-bluez.py > /out/mock.log 2>&1 &
    sleep 1
elif [ "$mode" = fake ]; then
    export READOUT_FAKE_BLE=1
fi
dbus-launch --exit-with-session /app/Readout > /out/app.log 2>&1 || echo "app exited with $?" >> /out/app.log
sleep infinity
