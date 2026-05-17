using System;
using ObjCRuntime;

namespace BodyCam.HeyCyan.iOS.Bindings;

/// <summary>
/// Bluetooth connection state (from OdmBleConstants.h BLECONNECTSTATE enum)
/// </summary>
[Native]
public enum BleConnectState : long
{
    Off = 0,  // BLECONNECTSTATEOFF
    On = 1,   // BLECONNECTSTATEON
    Fail = 2  // BLECONNECTSTATEFAIL
}

/// <summary>
/// Device operating modes (from QCDFU_Utils.h QCOperatorDeviceMode enum)
/// </summary>
[Native]
public enum QCOperatorDeviceMode : long
{
    Unknown = 0x00,
    Photo = 0x01,
    Video,
    VideoStop,
    Transfer,
    OTA,
    AiPhoto,
    SpeechRecognition,
    Audio,
    TransferStop,
    FactoryReset,
    SpeechRecognitionStop,
    AudioStop,
    FindDevice,
    Restart,
    NoPowerP2P,
    SpeakStart,
    SpeakStop,
    TranslateStart,
    TranslateStop,
}

/// <summary>
/// AI speaking modes (from QCDFU_Utils.h QGAISpeakMode enum)
/// </summary>
[Native]
public enum QGAISpeakMode : long
{
    Start = 0x01,
    Hold,
    Stop,
    ThinkingStart,
    ThinkingHold,
    ThinkingStop,
    NoNet = 0xf1,
}

/// <summary>
/// Volume modes (from QCVolumeInfoModel.h QCVolumeMode enum)
/// </summary>
[Native]
public enum QCVolumeMode : long
{
    Music = 0x01,
    Call = 0x02,
    System = 0x03
}

/// <summary>
/// DFU (Device Firmware Update) operation codes (from QCDFU_Utils.h ODM_DFU_Operation enum)
/// </summary>
[Native]
public enum OdmDfuOperation : long
{
    StartDfuRequest = 0x01,
    InitializeDfuParametersRequest = 0x02,
    ReceiveFirmwareImageRequest = 0x03,
    ValidateFirmwareRequest = 0x04,
    ActivateAndResetRequest = 0x05,
    CheckStatus = 0x06,
    SetupDeviceStatus = 0x40,
    SetDeviceMode = 0x41,
    GetDeviceBattery = 0x42,
    GetDeviceVersion = 0x43,
    VoiceWakeup = 0x44,
    VoiceHeartbeat = 0x45,
    WearingDetection = 0x46,
    DeviceConfig = 0x47,
    AISpeak = 0x48,
    Volume = 0x51,
    BTStatus = 0x52,
    OTAFileDownloadLink = 0xFC,
    Thumbnail = 0xFD,
    DataUpdate = 0x73,
}

/// <summary>
/// DFU operation status codes (from QCDFU_Utils.h ODM_DFU_OperationStatus enum)
/// </summary>
[Native]
public enum OdmDfuOperationStatus : long
{
    SuccessfulResponse = 0x00,
    WrongDataLengthResponse = 0X01,
    InvalidDataResponse = 0x02,
    WrongCommandStageResponse = 0x03,
    InvalidCommandParameterResponse = 0x04,
    DeviceInternalErrorResponse = 0x05,
    NotEnoughPowerResponse = 0x06,
    DialFileOverwhelmingResponse = 0x07
}

/// <summary>
/// DFU device process status (from QCDFU_Utils.h ODM_DFU_Device_Process_Status enum)
/// </summary>
[Native]
public enum OdmDfuDeviceProcessStatus : ulong
{
    Free = 0x00,
    ReadyToUpdate = 0x01,
    ParameterInited = 0x02,
    FirmwareReceiving = 0x03,
    FirmwareValidated = 0x04,
    NotKnown = 0x05
}

/// <summary>
/// DFU firmware type (from QCDFU_Utils.h ODM_DFU_FirmwareType enum)
/// </summary>
[Native]
public enum OdmDfuFirmwareType : long
{
    Application = 0x01,
    Bootloader = 0x02,
    Softdevice = 0x03,
}

/// <summary>
/// DFU band type (from QCDFU_Utils.h ODM_DFU_BandType enum)
/// </summary>
[Native]
public enum OdmDfuBandType : long
{
    TwoBand = 0x00,
    OneBand = 0x01,
}
