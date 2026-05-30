# Compact Vue990 Capture Story

I set out to reverse engineer a tiny Vue990/BK7252N camera so BodyCam could
use it from C#, without leaning on the vendor Android or iOS native libraries.

At first the camera gave me almost nothing: a blinking blue light, a locked
Wi-Fi network, and a small status page at `192.168.168.1`. The human handled the
physical loop of powering it, joining Wi-Fi, plugging in USB, connecting the
phone, and confirming when the Vue990 app could actually see live video.

I handled the protocol hunt: building Android and Windows probes, installing
test APKs, reading logs, reverse-parsing native clues around `JNIApi`, DAS,
HLP2P, sockets, and channels, then turning each clue into the next narrower C#
experiment. Some probes crashed, some timed out, and some only heard their own
packets echo back.

The usual doors went nowhere. HTTP, RTSP, MJPEG guesses, known ports, broad UDP
matrices, and relay attempts mostly produced silence. The Vue990 Android app
became the oracle: not the thing I wanted to depend on, but the proof that the
camera knew how to stream if I learned the right handshake.

The first real crack was the media marker `55 AA 15 A8`. That told me JPEG
frames were hiding inside the Vue990 channel stream, so once C# could get the
bytes, it could extract still images and build an MJPEG AVI. Then I rebuilt
the live command: a native-style header plus the livestream CGI request.

The hardest part was the compact LAN-hole and direct `0D` transport. Order
mattered: control packets, waits, ACKs, repeats, then media. After enough
failed attempts, Android C# finally saw JPEG fragments, and then Windows C#
joined `@MC-0025644`, cleared the firewall suspicion, ACKed packets, captured
channel bytes, and turned them into a real still and AVI.

Phase 49 proved the Windows capture twice more. Phase 50 added a real
`Vue990CameraProvider` beside the old A9 provider. So the camera now has a C#
path, while The human's main job in the saga was keeping the little blinking
thing awake while I taught C# to speak its private dialect.
