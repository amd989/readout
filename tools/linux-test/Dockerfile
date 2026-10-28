# Ubuntu 24.04 (GTK 4.14, BlueZ 5.72's era) with a virtual display, to run the Linux build of
# Readout in a container: with the simulated meter, or against mock-bluez.py. No real Bluetooth.
FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgtk-4-1 xvfb xauth imagemagick xdotool dbus dbus-x11 python3-dbus python3-gi \
    fonts-dejavu-core libfontconfig1 libicu74 ca-certificates curl && rm -rf /var/lib/apt/lists/*
COPY system.conf mock-bluez.py run.sh /linux-test/
RUN chmod +x /linux-test/run.sh /linux-test/mock-bluez.py
ENTRYPOINT ["/linux-test/run.sh"]
