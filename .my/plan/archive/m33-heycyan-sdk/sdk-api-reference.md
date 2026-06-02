# M33 — HeyCyan Android SDK API Reference

> Generated 2026-04-29 from the actual `glasses_sdk_20250723_v01.aar` binding
> at [src/BodyCam.HeyCyan.Android.Bindings/](../../../src/BodyCam.HeyCyan.Android.Bindings/)
> by inspecting `obj/Debug/net10.0-android/api.xml` plus the CyanBridge
> reference implementation under
> [Alternative-HeyCyan-App-and-SDK/android/CyanBridge/](../../../Alternative-HeyCyan-App-and-SDK/android/CyanBridge/).
>
> **Authoritative for all M33 implementation work.** When a phase or wave doc
> references SDK type/method names, validate against this file first.

---

## Section A — Public Types in the Binding

### Core Connection Management

#### `Com.Oudmon.Ble.Base.Bluetooth.BleBaseControl`
**Purpose:** Low-level Bluetooth discovery, pairing, GATT connection lifecycle.
**Singleton:** `BleBaseControl.GetInstance()` / `GetInstance(Context)`

| Method | Signature | Purpose |
|--------|-----------|---------|
| `Connect(string address)` | `bool` | Initiate BLE connection to device by MAC address |
| `DisconnectDevice(string address)` | `void` | Disconnect from device |
| `CreateBond(BluetoothDevice, int)` | `bool` | Pair device (transport = classic or BLE) |
| `SetListener(IBleListener)` | `void` | Register connection state listener |
| `IsmIsConnected()` | `bool` | Active connection check |
| `IsConnecting()` | `bool` | Connection in progress |

#### `Com.Oudmon.Ble.Base.Bluetooth.BleOperateManager`
**Purpose:** Queue BLE read/write operations, manage GATT notifications.
**Singleton:** `BleOperateManager.GetInstance()` / `GetInstance(Application)`
**Extends:** `HandlerThread` (background BLE I/O thread).

| Method | Signature | Purpose |
|--------|-----------|---------|
| `ConnectWithScan(string mac)` | `void` | Scan then connect |
| `ConnectDirectly(string mac)` | `void` | Connect without scan |
| `Disconnect()` | `void` | Disconnect |
| `Execute(BaseRequest)` | `bool` | Queue a BLE characteristic R/W request |
| `IsConnected()` | `bool` | Connected? |
| `IsReady()` | `bool` | GATT services discovered? |
| `AddNotifyListener(int key, ICommandResponse)` | `bool` | Register low-level notify listener |
| `RemoveNotifyListener(int key)` | `void` | Unregister |
| `AddOutDeviceListener(int type, ICommandResponse)` | `void` | Register high-level device event listener |
| `RemoveOutDeviceListener(int key)` | `void` | Unregister |

### Command & Data Handler

#### `Com.Oudmon.Ble.Base.Communication.LargeDataHandler`
**Purpose:** High-level command + parsed-response API.
**Singleton:** `LargeDataHandler.GetInstance()`

| Method | Purpose |
|--------|---------|
| `GlassesControl(byte[] cmd, ILargeDataResponse<GlassModelControlResponse>)` | Send raw byte command, parsed response |
| `SyncDeviceInfo(ILargeDataResponse<DeviceInfoResponse>)` | Query device version/model/serial |
| `SyncTime(ILargeDataResponse<SyncTimeResponse>)` | Send phone time to glasses |
| `SyncBattery()` | Request battery (async, via notify listener) |
| `SyncHeartBeat(int type)` | Keep-alive |
| `GetPictureThumbnails(ILargeDataImageResponse)` | Retrieve last photo thumbnail via BLE |
| `GetVolumeControl(ILargeDataResponse<VolumeControlResponse>)` | Read volume |
| `SetVolumeControl(int minMusic, int maxMusic, int currMusic, int minCall, int maxCall, int currCall, int sysMin, int sysMax, int sysCurr, int currVolumeType)` | Set volume |
| `WriteIpToSoc(string url, ILargeDataResponse<BatteryResponse>)` | Notify glasses of HTTP server URL for Wi-Fi transfer / OTA |
| `AddOutDeviceListener(int type, ILargeDataResponse)` | Register async device-notification listener |
| `RemoveOutDeviceListener(int type)` | Unregister |
| `AiVoiceWake(bool write, bool isOpen, ILargeDataResponse<GlassesAiVoiceRsp>)` | Enable/disable AI voice wake |
| `AiVoicePlay(int status, ILargeDataResponse<GlassesAiVoicePlayStatusRsp>)` | AI voice playback control |
| `WearCheck(bool write, bool isOpen, ILargeDataResponse<GlassesWearRsp>)` | Wear detection |
| `WearFunctionSupport(ILargeDataResponse<GlassesTouchSupportRsp>)` | Touch+wear support flags |
| `InitPackageNotify(ILargeDataResponse<AiChatResponse>)` | Initialize AI chat listener |
| `InitEnable()` / `DisEnable()` / `OpenBT()` / `CleanMap()` | Lifecycle |

**Action type constants** (for `AddOutDeviceListener`):
- `ACTION_SYNC_TIME = 0x40`
- `ACTION_GLASSES_CONTROL = 0x41`
- `ACTION_GLASSES_BATTERY = 0x42`
- `ACTION_DEVICE_INFO = 0x43`
- `ACTION_DEVICE_HEART_BEAT = 0x45`
- `ACTION_DEVICE_WEAR = 0x46`
- `ACTION_DEVICE_WEAR_SUPPORT = 0x47`
- `ACTION_VOICE_STATUS = 0x48`
- `ACTION_VOLUME_CONTROL = 0x51`
- `ACTION_DEVICE_NOTIFY = 100` (CyanBridge convention for the multiplexed notify channel — battery, IP, button events)
- `ACTION_DOWNLOAD_NOTIFY = 2` (CyanBridge convention for transfer-mode-specific notifications)

#### `Com.Oudmon.Ble.Base.Bluetooth.LargeDataParser`
**Singleton:** `LargeDataParser.GetInstance()`. Reassembles fragmented BLE notify frames.

#### `Com.Oudmon.Ble.Base.Bluetooth.DeviceManager`
**Singleton:** `DeviceManager.GetInstance()`. Holds connected device MAC + name.

### Callback Interfaces

- `Com.Oudmon.Ble.Base.Communication.ILargeDataResponse<T>` — `void ParseData(int cmdType, T response)`
- `Com.Oudmon.Ble.Base.Communication.ILargeDataImageResponse` — `void ParseData(int progress, bool isComplete, byte[] data)`
- `Com.Oudmon.Ble.Base.Bluetooth.IBleListener` — GATT lifecycle callbacks (`BleGattConnected`, `BleGattDisconnect`, `BleCharacteristicChanged`, `BleStatus`, `BleServiceDiscovered`, etc.)
- `Com.Oudmon.Ble.Base.Communication.Bigdata.Resp.GlassesDeviceNotifyListener : ILargeDataResponse<GlassesDeviceNotifyRsp>` — base class for parsed device notifications.

---

## Section B — Command Byte Sequences

All commands sent via `LargeDataHandler.GlassesControl(byte[], callback)`.

| Command | Bytes | Notes |
|---------|-------|-------|
| **Start photo mode** | `0x02 0x01 0x01` | Begin photo mode |
| **AI photo trigger** | `0x02 0x01 0x06 0x02 0x02` | Smart-capture / AI photo |
| **Set photo param** | `0x02 0x01 <value>` | Exposure/ISO etc. |
| **Stop photo** | `0x02 0x01 0x0B` | Exit photo mode |
| **Get battery (poll)** | `0x02 0x04` | Async result via notify |
| **Enter transfer mode** | `0x02 0x01 0x04` | Enable Wi-Fi Direct media download |
| **Exit transfer mode** | `0x02 0x01 0x09` | Disable transfer mode |
| **Reset P2P** | `0x02 0x01 0x0F` | Reset Wi-Fi-Direct state machine |

**No explicit byte sequences observed in CyanBridge for video start/stop or audio start/stop** — these may be subsumed under `0x02 0x01 0x01` photo mode with parameter variants, or not exposed by this SDK build. See [Open Questions](#section-e--open-questions).

---

## Section C — Notify-Frame Parsing

Frame layout in `GlassesDeviceNotifyRsp.LoadData`:

```
LoadData[0..5]  : header / metadata
LoadData[6]     : notification type
LoadData[7..]   : payload
```

| Type | Payload | Meaning |
|------|---------|---------|
| `0x02` | `[7]` button id | **AI photo button pressed** |
| `0x03` | `[7]==1` | **AI voice button pressed** |
| `0x04` | `[7..9]` progress | OTA firmware progress |
| `0x05` | `[7]=battery, [8]=charging` | Battery report |
| `0x08` | `[7..10]` IPv4 | **Glasses Wi-Fi IP for transfer** |
| `0x09` | `[7]` error code (255 = transient) | P2P/Wi-Fi error |
| `0x0C` | `[7]==1` | Pause event |
| `0x0D` | `[7]==1` | Unbind |
| `0x0E` | — | Memory low |
| `0x10` | — | Translation pause |
| `0x12` | mixed | Volume changed |

Listener registration (CyanBridge convention):

```csharp
// Multiplexed notify channel — battery, button, IP, errors
LargeDataHandler.GetInstance().AddOutDeviceListener(100, new MyDeviceNotifyListener());
// Transfer-only channel — IP + P2P error
LargeDataHandler.GetInstance().AddOutDeviceListener(2, new DownloadNotifyListener());
```

The "button" semantics for M33's `IButtonInputProvider` come from frames where
`LoadData[6] == 0x02` (AI-photo button) and `LoadData[6] == 0x03` (voice
button). The QCSDK iOS framework uses a combined `cmdType=2` channel; on
Android the same data arrives via the multiplexed `addOutDeviceListener(100)`
listener — adapt frame parsing accordingly.

---

## Section D — Mapping (Plan-Doc Vocabulary → Real API)

| Plan-doc symbol | Real Android API |
|-----------------|------------------|
| `LargeDataHandler.getInstance()` | `Com.Oudmon.Ble.Base.Communication.LargeDataHandler.GetInstance()` |
| `LargeDataHandler.glassesControl(byte[])` | `LargeDataHandler.GlassesControl(byte[], ILargeDataResponse<GlassModelControlResponse>)` — callback REQUIRED |
| `QCCentralManager` | **No 1:1.** Use `BleBaseControl` (scan/connect/pair) + `BleOperateManager` (connection lifecycle, notify) |
| `setNotifyListener(...)` | `BleOperateManager.AddNotifyListener(int key, ICommandResponse)` (low-level) **or** `LargeDataHandler.AddOutDeviceListener(int type, ILargeDataResponse)` (high-level — preferred) |
| `getDeviceVersionInfo` | `LargeDataHandler.SyncDeviceInfo(ILargeDataResponse<DeviceInfoResponse>)` |
| `getDeviceMedia` (count) | **No direct method.** Track via notify 0x05 / 0x08 deltas. `GetPictureThumbnails` retrieves the last thumbnail; for full media inventory, use the HTTP `media.config` after entering transfer mode. |
| `openWifiWithMode(QCOperatorDeviceModeTransfer)` | `LargeDataHandler.GlassesControl(new byte[] { 0x02, 0x01, 0x04 }, cb)` then `LargeDataHandler.WriteIpToSoc(httpUrl, cb)` (two-step) |
| `QCOperatorDeviceModePhoto` | `new byte[] { 0x02, 0x01, 0x01 }` |
| `QCOperatorDeviceModeAIPhoto` | `new byte[] { 0x02, 0x01, 0x06, 0x02, 0x02 }` |
| `QCOperatorDeviceModeVideo` / `Audio` | **Not located in CyanBridge byte log.** Hold for hardware probing; may be variant params under `0x02 0x01 ...`. |
| `QCOperatorDeviceModeTransfer` | `new byte[] { 0x02, 0x01, 0x04 }` |
| `cmdType=2` button (iOS) | Android: parse multiplexed notify (channel 100), `LoadData[6] == 0x02` (AI-photo button) or `0x03` (voice button) |

---

## Section E — Open Questions

1. **Video / audio capture** — no explicit byte sequence located in CyanBridge. Probe hardware once available; meanwhile, scaffold using `0x02 0x01 0x01` semantics and add Phase 1 hardware-investigation note.
2. **Callback threading** — `ILargeDataResponse.ParseData` runs on the BLE I/O thread (`BleOperateManager`'s `HandlerThread`). All session events must be marshalled onto a known `SynchronizationContext` before raising back to MAUI.
3. **Listener lifetime** — `AddOutDeviceListener` stores in a `ConcurrentHashMap`. Always pair add/remove on session dispose to avoid leaks.
4. **OTA** — `ACTION_OTA_SOC` exists; init bytes not yet documented. Out of scope for M33.
5. **P2P error 255** — non-fatal; reset P2P and continue. Other codes need user-facing handling.
6. **`media.config`** — plaintext HTTP `GET /files/media.config`, then `GET /files/<name>`. Allow cleartext via `network_security_config.xml`.
7. **Wear/touch** — `WearCheck` / `WearFunctionSupport` exist but unused in CyanBridge; defer.
8. **Volume control** — 10-int signature, semantics undocumented; defer.
9. **AI voice wake** — `AiVoiceWake` / `AiVoicePlay` exist; orthogonal to live conversation flow.
10. **HFP/A2DP audio** — completely outside this SDK; goes through standard Android `BluetoothA2dp` / `BluetoothHeadset` (M33 Phase 3 territory).
