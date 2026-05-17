# UAT — HeyCyan Glasses (M33 + M36)

**Milestones covered:** M33 (HeyCyan SDK Integration), M36 (Windows BLE + WiFi)
**Status:** Template — not yet executed

> Run each platform section independently. Record results in
> `TestResults/m33-phase7/<yyyy-mm-dd>/uat-<platform>.md`.

---

## Pre-flight (all platforms)

- [ ] Glasses charged ≥ 60 %, firmware version recorded: `________`
- [ ] Phone / PC unpaired from any prior glasses (clear BT cache)
- [ ] App built in **Release** config
- [ ] Create folder `TestResults/m33-phase7/<yyyy-mm-dd>/`

---

## A — Android

### A.1 BLE scan & connect (M33 P1)

- [ ] **A1** Open BodyCam → GlassesPage → Scan → HeyCyan appears with name, MAC, RSSI
- [ ] **A2** Tap device → Connect → status panel shows battery %, MAC, FW, HW
- [ ] **A3** Shell battery widget visible, matches status panel ± 1 %
- [ ] **A4** Media counts (Photos / Videos / Audio) shown, non-negative

### A.2 Camera — file-based snapshot (M33 P2)

- [ ] **A5** Tap "Take Photo" → BLE photo command sent, glasses capture image
- [ ] **A6** Transfer mode activates → WiFi-Direct group formed, glasses IP received via BLE notify
- [ ] **A7** Photo downloads over HTTP → JPG received, non-zero size, viewable
- [ ] **A8** Transfer mode exits cleanly → WiFi-Direct torn down, BLE session resumes
- [ ] **A9** Double-tap button → AI Photo captured + sent to `VisionAgent`, response received

### A.3 Audio — BT Classic routing (M33 P3)

- [ ] **A10** Start Realtime conversation → audio routes to glasses: mic = HFP/SCO, speaker = A2DP
- [ ] **A11** Speak into glasses mic → Realtime API receives audio, agent responds
- [ ] **A12** Agent response plays on glasses speaker, not phone
- [ ] **A13** Disconnect glasses mid-call → fallback to phone mic + speaker within 2 s
- [ ] **A14** Reconnect glasses → audio re-routes without user action

### A.4 Button input (M33 P4)

- [ ] **A15** Single-tap glasses button → default action fires (start/stop conversation)
- [ ] **A16** Double-tap glasses button → photo capture triggers
- [ ] **A17** Long-press glasses button → conversation ends cleanly
- [ ] **A18** Settings → Button config → all 3 gestures shown with configurable actions

### A.5 Recorded media pipeline (M33 P5)

- [ ] **A19** Record audio on glasses → `.opus` file created on glasses storage
- [ ] **A20** Record video on glasses → `.mp4` file created on glasses storage
- [ ] **A21** Bulk transfer mode → `media.config` lists all files, downloads succeed
- [ ] **A22** OPUS files playable → raw OPUS wrapped to Ogg, plays in OS media player
- [ ] **A23** Media gallery page → lists imported files, filter by type works

### A.6 Device manager & fallback (M33 P7)

- [ ] **A24** Cold-boot app → glasses widget hidden until connected
- [ ] **A25** Power off glasses mid-call → all 4 providers fall back (camera→phone, mic→phone, speaker→phone, button→none)
- [ ] **A26** Power glasses back on → auto-reconnect, all providers re-bind
- [ ] **A27** Manual disconnect from GlassesPage → returns to scan list, shell widget hidden
- [ ] **A28** Battery widget: charging bolt appears within 1 s of placing on cradle
- [ ] **A29** Battery widget: low-battery red tint at ≤ 15 % when not charging

---

## B — iOS

### B.1 BLE scan & connect (M33 P6 + P1 parity)

- [ ] **B1** GlassesPage → Scan → HeyCyan appears (CBCentralManager discovery)
- [ ] **B2** Tap device → Connect → status panel shows battery, MAC, FW, HW
- [ ] **B3** Shell battery widget visible, matches status panel ± 1 %

### B.2 Camera — hotspot transfer (M33 P6 + P2 parity)

- [ ] **B4** Take Photo → BLE photo command sent
- [ ] **B5** Transfer mode → `NEHotspotConfiguration` joins glasses WiFi
- [ ] **B6** Photo downloads over HTTP → JPG received, viewable
- [ ] **B7** AI Photo via button double-tap → photo → VisionAgent round-trip

### B.3 Audio — BT Classic routing (M33 P3 parity)

- [ ] **B8** Start conversation → audio routes to glasses HFP + A2DP
- [ ] **B9** Speak + receive response → bidirectional audio through glasses
- [ ] **B10** Disconnect mid-call → fallback to phone within 2 s

### B.4 Button + recorded media (M33 P4/P5 parity)

- [ ] **B11** Single-tap / double-tap / long-press → all 3 gestures fire correct actions
- [ ] **B12** Bulk media transfer → files download via hotspot HTTP
- [ ] **B13** OPUS playable after Ogg wrap → audio plays in iOS media player

### B.5 Fallback (M33 P7 parity)

- [ ] **B14** Power off glasses mid-call → fallback within 2 s, call continues
- [ ] **B15** Power on → auto-reconnect → all providers re-bind
- [ ] **B16** Manual disconnect → scan list shown, widget hidden

---

## C — Windows (M36)

### C.1 BLE scan & connect (M36 P2)

- [ ] **C1** GlassesPage → Scan → `BluetoothLEAdvertisementWatcher` finds HeyCyan device
- [ ] **C2** Tap device → Connect → GATT service `de5bf728-…` discovered, write + notify chars bound
- [ ] **C3** Status panel populated → battery %, FW, HW from Device Information Service
- [ ] **C4** Shell battery widget visible and updating

### C.2 BLE commands (M36 P2)

- [ ] **C5** Send time sync → `0x03` + 4-byte timestamp written to `de5bf72a-…`
- [ ] **C6** Get media count → count returned via notify char `de5bf729-…`
- [ ] **C7** Battery notify → battery % received on notify characteristic
- [ ] **C8** Button events → tap/double-tap/long-press notify frames parsed correctly

### C.3 Camera — WiFi transfer (M36 P3)

- [ ] **C9** Take Photo via BLE → photo command `0x02 0x01 0x01` sent successfully
- [ ] **C10** Enter transfer mode → `0x02 0x01 0x04` sent, glasses start WiFi hotspot
- [ ] **C11** Glasses IP received via BLE → notify frame `loadData[6]==0x08`, IP in bytes 7-10
- [ ] **C12** Windows joins glasses WiFi → `WiFiAdapter.ConnectAsync` or WLAN P/Invoke succeeds
- [ ] **C13** `media.config` fetched → `GET http://{ip}/files/media.config` returns file list
- [ ] **C14** Photo downloaded → `GET http://{ip}/files/{name}` returns JPG, viewable
- [ ] **C15** Exit transfer mode → `0x02 0x01 0x09` sent, WiFi torn down, BLE resumes

### C.4 Audio routing (Windows BT Classic)

- [ ] **C16** Start conversation → audio routes to glasses (Windows BT audio endpoint)
- [ ] **C17** Bidirectional audio → speak into glasses mic, API response on glasses speaker
- [ ] **C18** Fallback on disconnect → audio reverts to default Windows device within 2 s

### C.5 Integration (M36 P4)

- [ ] **C19** DI registration → `WindowsHeyCyanGlassesSession` resolves (not `NullHeyCyanGlassesSession`)
- [ ] **C20** App manifest → `bluetooth` + `wifiControl` capabilities present
- [ ] **C21** Full flow: scan → connect → photo → transfer → disconnect end-to-end without manual intervention
- [ ] **C22** Button → AI Photo → VisionAgent → double-tap triggers photo, transfers, sends to agent
- [ ] **C23** Recorded media transfer → bulk download via WiFi, OPUS wrapped, playable

---

## D — Cross-platform parity

- [ ] **D1** `IHeyCyanGlassesSession` API surface identical across Android, iOS, Windows
- [ ] **D2** `HeyCyanFrameParser` shared — no platform fork
- [ ] **D3** `HeyCyanMediaTransfer` shared — no platform fork
- [ ] **D4** `HeyCyanCommands` byte sequences identical across platforms
- [ ] **D5** Button gesture mapping identical across platforms
- [ ] **D6** Battery / version / media counts match across platforms (same glasses, same values)
- [ ] **D7** Fallback latency ≤ 2 s on all platforms

---

## E — Edge cases & error handling

- [ ] **E1** Scan with glasses powered off → empty scan list, no crash
- [ ] **E2** Connect timeout (glasses out of range) → timeout error, state returns to Disconnected
- [ ] **E3** Transfer mode with no media on glasses → `media.config` returns empty list, no error
- [ ] **E4** WiFi join failure (signal too weak) → timeout, state returns to Connected (BLE), error surfaced to UI
- [ ] **E5** Glasses battery dies mid-transfer → transfer aborts gracefully, partial file cleaned up
- [ ] **E6** Multiple rapid button presses → events debounced by firmware, no duplicate actions
- [ ] **E7** BLE notify frame with `loadData[6]==0x09` → logged as non-fatal P2P error, no crash
- [ ] **E8** Airplane mode toggled mid-session → disconnect detected, fallback fires, UI updates
- [ ] **E9** Second pair of glasses in range during scan → both appear in list, only selected one connects
- [ ] **E10** App backgrounded during transfer → transfer completes or times out cleanly

---

## Sign-off

- [ ] **Android** — Tester: ________ Date: ________ Blockers: ________
- [ ] **iOS** — Tester: ________ Date: ________ Blockers: ________
- [ ] **Windows** — Tester: ________ Date: ________ Blockers: ________
