# The Blue Blinkenlight Saga

There are projects that begin with a clean spec, a neat API, and a satisfying
diagram.

This was not one of those projects.

This one began with a tiny camera, a blinking blue light, and a Wi-Fi network
with a name that looked like it had escaped from a factory label:
`@MC-0025644`.

The light blinked. Then it stopped blinking. Then it blinked again. This was
the camera's entire user interface, and it had the emotional range of a BIOS
beep. Still, it was clearly trying to tell us something.

We started, as respectable engineers do, by trying all the reasonable things.
HTTP? No stream. RTSP? No stream. MJPEG endpoints? No stream. A selection of
ports, probes, polite requests, less polite requests, and carefully formatted
packets? Mostly silence.

The camera sat at `192.168.168.1` like a small black box with tenure.

It would answer `get_status.cgi`, at least. That was our first proper clue. It
introduced itself as `BK7252N`, with the real device id `BK0025644WBPD`, and it
accepted `admin` / `888888` with the confidence of firmware that had never met
a threat model it particularly respected.

So we knew it was alive.

We just could not persuade it to show us the picture.

Then the Android phone entered the story. The Vue990 app connected. The live
image appeared. Somewhere inside that app was the working spell. The camera
would stream, but only after the correct secret handshake, and the handshake was
not "GET /video.jpg" like a civilized device from a better timeline.

The next phase was less like ordinary programming and more like protocol
archaeology with a solderless keyboard.

This is where the division of labor became wonderfully practical. You did the
physical-world debugging: turning the camera on, watching the blue light,
connecting the phone, switching Wi-Fi networks, plugging in USB, and telling me
when the vendor app could see a real picture. I did the code-world digging:
building probes, installing test APKs, reading logs, reverse-parsing native
behavior, comparing packet captures, adding C# harnesses, and turning each clue
into the next experiment.

We installed tools. We built probes. They crashed. We fixed permissions.
Android said UDP was not allowed until it was. The phone said it was connected
to the camera Wi-Fi. Windows said it could see different things depending on
whether it was on the right network, the wrong network, wired, wireless, or
just being Windows with a clipboard.

For a while, the investigation had the structure of a detective board, except
all the string was hex.

The vendor app used native libraries. The native libraries used Vue990/PPCS
session machinery. The session machinery used something called HLP2P. HLP2P
used compact LAN-hole packets. The compact LAN-hole packets seemed to involve
tiny UDP messages with names that sounded cheerful until they did nothing.

So I treated the vendor stack as an oracle. I reverse-parsed the code paths
around `JNIApi`, `ConnectByServer`, DAS parameters, HLP2P helpers, socket
traffic, channel reads, and command writes. Each time the camera refused to
answer, I wrote another narrower probe instead of guessing wider. Eventually
the noisy pile of failed attempts turned into a map.

We tried broad UDP discovery. We tried relay candidates. We decoded DAS server
parameters. We poked at TCP `65527`. We sent packet shapes that looked right
and received either nothing or, worse, our own packets reflected back at us like
the network equivalent of a sarcastic mirror.

Then the first real breakthrough arrived: the native app was not streaming
H.264. It was sending JPEG frames inside a Vue990 channel envelope that began
with:

```text
55 AA 15 A8
```

This was a beautiful moment. Not because `55 AA 15 A8` is beautiful in any
normal sense, but because it meant the media was not mysterious anymore. Once
we had channel bytes, C# could extract JPEGs. Once C# could extract JPEGs, C#
could write a still image. Once C# could write a stack of JPEGs, C# could make
an MJPEG AVI.

The media was solved.

The door was still locked.

Next came the command-channel discovery. The Vue990 app did not simply send raw
HTTP into the tunnel. It sent an 8-byte little-endian command header on channel
`0`, followed by a credentialed CGI request:

```text
GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&
```

This was the first time C# could speak a real sentence in the camera's private
language. The native stack still carried the session, but C# could now say:
"please start the stream", and the camera listened.

That gave us image and video through a native-backed path. It was progress, but
not victory. The goal was C#.

So we went after the transport.

This is where the story gets wonderfully strange.

The working native path did not behave like the broad probe attempts. It used a
compact local sequence: LAN-hole probe, LAN-hole response, ACK, ready packet,
alive probe, alive ACK, and then a series of direct `0D` packets. It was not a
grand protocol cathedral. It was more like a secret knock followed by a very
specific rhythm on the table.

And the rhythm mattered.

At first, C# got close. It reached the camera. It performed pieces of the
handshake. It sent controls. It received responses. But no media came. The
camera would acknowledge some packets and then become very committed to not
sending JPEGs.

We learned the order was not decorative.

Send control `0`. Send control `1`. Wait. Send control `2`. Send control `3`.
Wait. Repeat control `1`. ACK the large response. Then repeat control `3`.

That was the cadence.

When Android C# finally followed that native-paced order, the camera answered
with the missing response, then the `55 AA 15 A8` media marker, then JPEG
fragments. The stream opened. The frames arrived. A still image landed on disk.
Then a short MJPEG AVI followed it.

That was Phase 47, and it felt like the moment the packet cave finally stopped
echoing and started playing video.

But there was one more mountain: Windows.

Until then, Android had been the proving ground because the phone could sit on
the camera Wi-Fi while Windows orchestrated over ADB. Useful, yes. Elegant,
debatable. The real goal was Windows C# directly talking to the camera.

So the laptop joined `@MC-0025644`.

This is where the old firewall suspicion returned with dramatic timing.
Everyone looked at Windows Firewall. Windows Firewall looked back wearing a
perfectly innocent expression. We tested anyway.

The laptop reached `192.168.168.1`. It fetched status. It sent the compact
LAN-hole probe. The camera responded. It sent ready. It accepted ACKs. Direct
packets arrived.

Windows was not the villain this time. Plot twist.

The first Windows run captured raw channel bytes but did not extract frames in
the live loop. Close, but not enough. So we added fallback extraction from the
saved channel dump. The next run produced the thing we had been chasing:

- `managed-direct-still.jpg`
- `managed-direct-video-mjpeg.avi`

From Windows. In C#. Directly from the camera.

No Android relay. No vendor app in the runtime path. No native Vue990/PPCS
session calls carrying the stream.

Then came the cleanup. The packet bytes that used to feel like anonymous
incantations were given names:

- `initial-short-request`
- `initial-long-request`
- `media-short-request`
- `media-long-request`

They moved into `A9Vue990PostHoleControlProvider`, a little cabinet with labels
on the drawers. The Windows client and Android probe both used the same
provider. Tests checked order, lengths, defensive copies, and direct packet
fields. The normal logs stopped saying "replay control" and started saying the
more honest "post-hole control".

Finally, we proved it twice more on Windows.

Two fresh Phase 49 runs. Different camera-side UDP ports. Different LAN-hole
status values. Same result: real JPEG, real MJPEG AVI, hashes recorded, paths
documented.

At the end, the tiny camera was still blinking its inscrutable blue light, but
the balance of power had changed. We were no longer asking it nicely through
the vendor app. We had learned enough of its private dialect to open the stream
ourselves.

The final state is gloriously engineer-shaped:

- The practical goal is done for this camera.
- C# can download image and video on Windows.
- Android C# also proved the same managed direct sequence.
- The media path is understood.
- The compact direct transport is implemented.
- The remaining caveat is precise: the four encrypted post-hole controls are
  scoped native-observed vectors, not yet derived from first principles for
  every possible Vue990/BK7252N variant.

Which is to say: the lock at the gate is open, and the last remaining task is
writing a nicer map for neighboring firmware variants.

Not bad for something that started as:

```text
blue light blinking
blue light stopped
can you see the Wi-Fi?
```

Sometimes reverse engineering is a straight road.

This time it was a packet maze with a camera at the center, a phone holding the
first clue, Windows unexpectedly behaving itself, and C# walking out with the
JPEG.
