using System;
using CoreBluetooth;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace BodyCam.HeyCyan.iOS.Bindings;

// QCSDKManagerDelegate — protocol for receiving SDK events
[Protocol, Model]
[BaseType(typeof(NSObject))]
interface QCSDKManagerDelegate
{
    [Export("didUpdateBatteryLevel:charging:")]
    void DidUpdateBatteryLevel(nint battery, bool charging);

    [Export("didUpdateMediaWithPhotoCount:videoCount:audioCount:type:")]
    void DidUpdateMedia(nint photoCount, nint videoCount, nint audioCount, nint type);

    [Export("didUpdateWiFiUpgradeProgressWithDownload:upgrade1:upgrade2:")]
    void DidUpdateWifiUpgradeProgress(nint download, nint upgrade1, nint upgrade2);

    [Export("didReceiveWiFiUpgradeResult:")]
    void DidReceiveWifiUpgradeResult(bool success);

    [Export("didReceiveAIChatImageData:")]
    void DidReceiveAiChatImageData(NSData imageData);
}

// QCSDKManager — main SDK singleton manager
[BaseType(typeof(NSObject))]
interface QCSDKManager
{
    [Static]
    [Export("shareInstance")]
    QCSDKManager SharedInstance { get; }

    [Export("debug")]
    bool Debug { get; set; }

    [NullAllowed, Export("delegate", ArgumentSemantic.Weak)]
    NSObject WeakDelegate { get; set; }

    [Wrap("WeakDelegate")]
    [NullAllowed]
    QCSDKManagerDelegate Delegate { get; set; }

    [Export("addPeripheral:finished:")]
    void AddPeripheral(CBPeripheral peripheral, Action<bool> finished);

    [Export("removePeripheral:")]
    void RemovePeripheral(CBPeripheral peripheral);

    [Export("removeAllPeripheral")]
    void RemoveAllPeripherals();
}

// QCSDKCmdCreator — command creator for device operations
[BaseType(typeof(NSObject))]
interface QCSDKCmdCreator
{
    [Static]
    [Export("setDeviceMode:success:fail:")]
    void SetDeviceMode(QCOperatorDeviceMode mode, Action success, Action<nint> fail);

    [Static]
    [Export("openWifiWithMode:success:fail:")]
    void OpenWifi(QCOperatorDeviceMode mode, Action<NSString, NSString> success, Action<nint> fail);

    [Static]
    [Export("setVideoInfo:duration:success:fail:")]
    void SetVideoInfo(nint angle, nint duration, Action success, Action fail);

    [Static]
    [Export("getVideoInfoSuccess:fail:")]
    void GetVideoInfo(Action<nint, nint> success, Action fail);

    [Static]
    [Export("getDeviceWifiIPSuccess:failed:")]
    void GetDeviceWifiIp([NullAllowed] Action<NSString> success, [NullAllowed] Action fail);

    [Static]
    [Export("getDeviceMedia:fail:")]
    void GetDeviceMedia(Action<nint, nint, nint, nint> success, Action fail);

    [Static]
    [Export("deleleteAllMediasSuccess:fail:")]
    void DeleteAllMedias(Action success, Action fail);

    [Static]
    [Export("deleleteMedia:success:fail:")]
    void DeleteMedia(string name, Action success, Action fail);

    [Static]
    [Export("setAudioInfo:duration:success:fail:")]
    void SetAudioInfo(nint angle, nint duration, Action success, Action fail);

    [Static]
    [Export("getAudioInfoSuccess:fail:")]
    void GetAudioInfo(Action<nint, nint> success, Action fail);

    [Static]
    [Export("getDeviceBattery:fail:")]
    void GetDeviceBattery(Action<nint, bool> success, Action fail);

    [Static]
    [Export("getDeviceVersionInfoSuccess:fail:")]
    void GetDeviceVersionInfo(Action<NSString, NSString, NSString, NSString> success, Action fail);

    [Static]
    [Export("isPeripheralFreeNow")]
    bool IsPeripheralFreeNow { get; }

    // DFU methods
    [Static]
    [Export("switchToDFU:")]
    void SwitchToDfu([NullAllowed] Action<NSError> finished);

    [Static]
    [Export("initDFUFirmwareType:binFileSize:checkSum:crc16:finished:")]
    void InitDfu(OdmDfuFirmwareType type, uint binFileSize, ushort checkSum, ushort crc16, [NullAllowed] Action<NSError> finished);

    [Static]
    [Export("sendFilePacketData:serialNumber:finished:")]
    void SendFilePacketData(NSData packetData, nuint sn, [NullAllowed] Action<nuint, NSError> finished);

    [Static]
    [Export("checkMyFirmwareWithData:finished:")]
    void CheckMyFirmware([NullAllowed] NSData data, [NullAllowed] Action<NSError> finished);

    [Static]
    [Export("finishDFU:")]
    void FinishDfu([NullAllowed] Action<NSError> finished);

    [Static]
    [Export("checkCurrentStatusWithData:finished:")]
    void CheckCurrentStatus([NullAllowed] NSData data, [NullAllowed] Action<OdmDfuDeviceProcessStatus, NSError> finished);

    [Static]
    [Export("getDFUBandTypeInfoSuccess:fail:")]
    void GetDfuBandTypeInfo([NullAllowed] Action<OdmDfuBandType, bool> getData, [NullAllowed] Action fail);

    [Static]
    [Export("switchToOneBandDFU:")]
    void SwitchToOneBandDfu([NullAllowed] Action<NSError> finished);

    // OTA / Config / Misc
    [Static]
    [Export("sendOTAFileLink:finished:")]
    void SendOtaFileLink(string downloadUrl, Action<bool, NSError> finished);

    [Static]
    [Export("setupDeviceDateTime:")]
    void SetupDeviceDateTime(Action<bool, NSError> finished);

    [Static]
    [Export("getThumbnail:success:fail:")]
    void GetThumbnail(nint pocket, Action<NSData, nint, nint> success, Action fail);

    [Static]
    [Export("sendVoiceHeartbeatWithFinished:")]
    void SendVoiceHeartbeat(Action<bool, NSError> finished);

    [Static]
    [Export("getVoiceWakeupWithFinished:")]
    void GetVoiceWakeup(Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("setVoiceWakeup:finished:")]
    void SetVoiceWakeup(bool isOn, Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("getWearingDetectionWithFinished:")]
    void GetWearingDetection(Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("setWearingDetection:finished:")]
    void SetWearingDetection(bool isOn, Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("getDeviceConfigWithFinished:")]
    void GetDeviceConfig(Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("setAISpeekModel:finished:")]
    void SetAiSpeakModel(QGAISpeakMode model, Action<bool, NSError> finished);

    [Static]
    [Export("getVolumeWithFinished:")]
    void GetVolume(Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("setVolume:finished:")]
    void SetVolume(QCVolumeInfoModel infoModel, Action<bool, NSError, NSObject> finished);

    [Static]
    [Export("setBTStatus:finished:")]
    void SetBTStatus(bool isOpen, Action<bool, NSError> finished);

    [Static]
    [Export("getBTStatusWithFinished:")]
    void GetBTStatus(Action<bool, NSError, NSObject> finished);
}

// QCVersionHelper — version helper
[BaseType(typeof(NSObject))]
interface QCVersionHelper
{
    [Static]
    [Export("frameworkVersion")]
    string FrameworkVersion { get; }
}

// QCVolumeInfoModel — volume configuration model
[BaseType(typeof(NSObject))]
interface QCVolumeInfoModel
{
    [Export("musicMin")]
    nint MusicMin { get; set; }

    [Export("musicMax")]
    nint MusicMax { get; set; }

    [Export("musicCurrent")]
    nint MusicCurrent { get; set; }

    [Export("callMin")]
    nint CallMin { get; set; }

    [Export("callMax")]
    nint CallMax { get; set; }

    [Export("callCurrent")]
    nint CallCurrent { get; set; }

    [Export("systemMin")]
    nint SystemMin { get; set; }

    [Export("systemMax")]
    nint SystemMax { get; set; }

    [Export("systemCurrent")]
    nint SystemCurrent { get; set; }

    [Export("mode")]
    QCVolumeMode Mode { get; set; }
}

// Constants from OdmBleConstants.h
[Static]
interface OdmBleConstants
{
    [Field("QCSDKSERVERUUID1", "__Internal")]
    NSString QcsdkServerUuid1 { get; }

    [Field("QCSDKSERVERUUID2", "__Internal")]
    NSString QcsdkServerUuid2 { get; }

    [Field("OdmNotifyD2P", "__Internal")]
    NSString OdmNotifyD2P { get; }

    [Field("OdmNotifyD2PDataKey", "__Internal")]
    NSString OdmNotifyD2PDataKey { get; }

    [Field("OdmBleConnectState", "__Internal")]
    NSString OdmBleConnectState { get; }
}
