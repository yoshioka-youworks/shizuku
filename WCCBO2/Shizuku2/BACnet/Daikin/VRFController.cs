﻿using BaCSharp;
using System.IO.BACnet;

namespace Shizuku2.BACnet.Daikin
{
  /// <summary>ダイキン用VRFコントローラ</summary>
  public class VRFController : IBACnetController
  {

    #region 定数宣言

    const uint DEVICE_ID = 2;

    public const int EXCLUSIVE_PORT = 0xBAC0 + (int)DEVICE_ID;

    const string DEVICE_NAME = "Daikin VRF controller";

    const string DEVICE_DESCRIPTION = "Daikin VRF controller";

    const int SIGNAL_UPDATE_SPAN = 60;

    #endregion

    #region 列挙型

    private enum ObjectNumber
    {
      AnalogInput = 0 * 4194304,
      AnalogOutput = 1 * 4194304,
      AnalogValue = 2 * 4194304,
      BinaryInput = 3 * 4194304,
      BinaryOutput = 4 * 4194304,
      BinaryValue = 5 * 4194304,
      MultiStateInput = 13 * 4194304,
      MultiStateOutput = 14 * 4194304,
      Accumulator = 23 * 4194304
    }

    private enum MemberNumber
    {
      OnOff_Setting = 1,
      OnOff_Status = 2,
      Alarm = 3,
      MalfunctionCode = 4,
      OperationMode_Setting = 5,
      OperationMode_Status = 6,
      FanSpeed_Setting = 7,
      FanSpeed_Status = 8,
      MeasuredRoomTemperature = 9,
      Setpoint = 10,
      FilterSignSignal = 11,
      FilterSignSignalReset = 12,
      RemoteControllerPermittion_OnOff = 13,
      RemoteControllerPermittion_OperationMode = 14,
      RemoteControllerPermittion_Setpoint = 16,
      CentralizedControl = 17,
      AccumulatedGas = 18,
      AccumulatedPower = 19,
      CommunicationStatus = 20,
      ForcedSystemStop = 21,
      AirflowDirection_Setting = 22,
      AirflowDirection_Status = 23,
      ForcedThermoOff_Setting = 24,
      ForcedThermoOff_Status = 25,
      EnergySaving_Setting = 26,
      EnergySaving_Status = 27,
      ThermoOn_Status = 28,
      Compressor_Status = 29,
      IndoorFan_Status = 30,
      Heater_Status = 31,
      VentilationMode_Setting = 32,
      VentilationMode_Status = 33,
      VentilationAmount_Setting = 34,
      VentilationAmount_Status = 35
    }

    #endregion

    #region インスタンス変数・プロパティ

    private BACnetCommunicator2 communicator;

    /// <summary>室内機の台数を取得する</summary>
    public int NumberOfIndoorUnits
    { get { return vrfUnitIndices.Length; } }

    private readonly VRFUnitIndex[] vrfUnitIndices;

    private readonly ExVRFSystem[] vrfSystems;

    private DateTime nextSignalApply = new DateTime(1980, 1, 1, 0, 0, 0);
    private DateTime nextSignalRead = new DateTime(1980, 1, 1, 0, 0, 0);

    #endregion

    #region コンストラクタ

    public VRFController(ExVRFSystem[] vrfs)
    {
      vrfSystems = vrfs;

      List<VRFUnitIndex> vrfInd = new List<VRFUnitIndex>();
      for (int i = 0; i < vrfs.Length; i++)
        for (int j = 0; j < vrfs[i].VRFSystem.IndoorUnitNumber; j++)
          vrfInd.Add(new VRFUnitIndex(i, j));
      vrfUnitIndices = vrfInd.ToArray();

      //DMS502B71が扱える台数は256台まで
      if (256 <= NumberOfIndoorUnits)
        throw new Exception("Invalid indoor unit number");

      communicator = new BACnetCommunicator2
        (makeDeviceObject(), EXCLUSIVE_PORT);
    }

    /// <summary>BACnet Deviceを作成する</summary>
    private DeviceObject makeDeviceObject()
    {
      DeviceObject dObject = new DeviceObject(DEVICE_ID, DEVICE_NAME, DEVICE_DESCRIPTION, true);

      for (int iuNum = 0; iuNum < NumberOfIndoorUnits; iuNum++)
      {
        dObject.AddBacnetObject(new BinaryOutput
          (getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.OnOff_Setting),
          "StartStopCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to start (On)/stop (Off) the indoor unit.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.OnOff_Status),
          "StartStopStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s On/Off status.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Alarm),
          "Alarm_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s normal/malfunction status.", false));

        dObject.AddBacnetObject(new MultiStateInput
          (getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.MalfunctionCode),
          "MalfunctionCode_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the malfunction code of an indoor unit in malfunction status.", 512, 1, false));

        dObject.AddBacnetObject(new MultiStateOutput
          (getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.OperationMode_Setting),
          "AirConModeCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set an indoor unit’s operation mode.", 3, 5));

        dObject.AddBacnetObject(new MultiStateInput
          (getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.OperationMode_Status),
          "AirConModeStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor an indoor unit’s operation mode.", 5, 3, false));

        dObject.AddBacnetObject(new MultiStateOutput
          (getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.FanSpeed_Setting),
          "AirFlowRateCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set an indoor unit’s fan speed.", 2, 4));

        dObject.AddBacnetObject(new MultiStateInput
          (getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.FanSpeed_Status),
          "AirFlowRateStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s fan speed.", 4, 2, false));

        dObject.AddBacnetObject(new AnalogInput<float>
          (getInstanceNumber(ObjectNumber.AnalogInput, iuNum, MemberNumber.MeasuredRoomTemperature),
          "RoomTemp_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the room temperature detected by the indoor unit return air sensor, remote sensor, or remote controller sensor.", 24, BacnetUnitsId.UNITS_DEGREES_CELSIUS));

        dObject.AddBacnetObject(new AnalogValue<float>
          (getInstanceNumber(ObjectNumber.AnalogValue, iuNum, MemberNumber.Setpoint),
          "TempAdjest_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set the indoor unit’s setpoint.", 24, BacnetUnitsId.UNITS_DEGREES_CELSIUS, false)
        { m_PROP_HIGH_LIMIT = 32, m_PROP_LOW_LIMIT = 16 });

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.FilterSignSignal),
          "FilterSign_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s filter sign status.", false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.FilterSignSignalReset),
          "FilterSignReset_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to reset the indoor unit’s filter sign signal.", false, false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_OnOff),
          "RemoteControlStart_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to permit or prohibit the On/Off operation from the remote controller used to start/stop the indoor unit.", false, false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_OperationMode),
          "RemoteContorlAirConModeSet_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to permit or prohibit the remote controller from changing the indoor unit’s operation mode.", false, false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_Setpoint),
          "RemoteControlTempAdjust_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to permit or prohibit the remote controller to set the indoor unit setpoint.", false, false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.CentralizedControl),
          "CL_Rejection_X" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to disable or enable control by the Daikin Centralized Controllers which includes the Intelligent Touch Controller used on each DIII-Net system (up to 4 DIII-Net system can be connected to the Interface for use in BACnet).", false, false));

        dObject.AddBacnetObject(new Accumulator<float>
          (getInstanceNumber(ObjectNumber.Accumulator, iuNum, MemberNumber.AccumulatedGas),
          "GasTotalPower_" + vrfUnitIndices[iuNum].ToString(),
          "No description.", 0, BacnetUnitsId.UNITS_CUBIC_METERS));

        dObject.AddBacnetObject(new Accumulator<float>
          (getInstanceNumber(ObjectNumber.Accumulator, iuNum, MemberNumber.AccumulatedPower),
          "ElecTotalPower_" + vrfUnitIndices[iuNum].ToString(),
          "No description.", 0, BacnetUnitsId.UNITS_KILOWATT_HOURS));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.CommunicationStatus),
          "CommunicationStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the communication status between the Interface for use in BACnet and the indoor units.", false));

        dObject.AddBacnetObject(new BinaryValue
          (getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.ForcedSystemStop),
          "SystemForcedOff_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to stop all the indoor units connected to the specified DIII network port and permits/prohibits the On/Off operation from the connected remote controller.", false, false));

        dObject.AddBacnetObject(new AnalogValue<float>
          (getInstanceNumber(ObjectNumber.AnalogValue, iuNum, MemberNumber.AirflowDirection_Setting),
          "AirDirectionCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to change the indoor unit’s airflow direction.", 0, BacnetUnitsId.UNITS_NO_UNITS, false));

        dObject.AddBacnetObject(new AnalogInput<float>
          (getInstanceNumber(ObjectNumber.AnalogInput, iuNum, MemberNumber.AirflowDirection_Status),
          "AirDirectionStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s airflow direction setting.", 0, BacnetUnitsId.UNITS_NO_UNITS));

        dObject.AddBacnetObject(new BinaryOutput
          (getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.ForcedThermoOff_Setting),
          "ForcedThermoOFFCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to force the indoor unit to operate without actively cooling or heating.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.ForcedThermoOff_Status),
          "ForcedThermoOFFStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor whether or not the indoor unit is forced to operate without actively cooling or heating.", false));

        dObject.AddBacnetObject(new BinaryOutput
          (getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.EnergySaving_Setting),
          "EnergyEfficiencyCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to instruct the indoor unit to operate at a temperature offset of 3.6 0F (20C) from the setpoint for saving energy. The actual setpoint is not changed.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.EnergySaving_Status),
          "EnergyEfficiencyStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor whether or not the indoor unit is operating at a temperature offset of 3.6 0F (20C) from the setpoint for saving energy.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.ThermoOn_Status),
          "ThermoStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor if the indoor unit is actively cooling or heating.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Compressor_Status),
          "CompressorStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the compressor status of the outdoor unit connected to the indoor unit.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.IndoorFan_Status),
          "IndoorFanStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the indoor unit’s fan status.", false));

        dObject.AddBacnetObject(new BinaryInput
          (getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Heater_Status),
          "HeaterStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the heater status commanded by the indoor unit logic.", false));

        dObject.AddBacnetObject(new MultiStateOutput
          (getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.VentilationMode_Setting),
          "VentilationModeCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set the Energy Recovery Ventilator’s Ventilation Mode.", 2, 3));

        dObject.AddBacnetObject(new MultiStateInput
          (getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.VentilationMode_Status),
          "VentilationModeStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set the Energy Recovery Ventilator’s Ventilation Mode.", 3, 2, false));

        dObject.AddBacnetObject(new MultiStateOutput
          (getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.VentilationAmount_Setting),
          "VentilationAmountCommand_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to set the Energy Recovery Ventilator’s Ventilation Amount.", 2, 6));

        dObject.AddBacnetObject(new MultiStateInput
          (getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.VentilationAmount_Status),
          "VentilationAmountStatus_" + vrfUnitIndices[iuNum].ToString(),
          "This object is used to monitor the Energy Recovery Ventilator’s Ventilation Amount.", 6, 2, false));
      }

      return dObject;
    }

    private int getInstanceNumber
      (ObjectNumber objNumber, int iUnitNumber, MemberNumber memNumber)
    {
      //DBACSではこの番号で管理しているようだが、これでは桁が大きすぎる。
      //return (int)objNumber + iUnitNumber * 256 + (int)memNumber; 
      return iUnitNumber * 256 + (int)memNumber;
    }

    #endregion

    #region IBACnetController実装

    /// <summary>制御値を機器やセンサに反映する</summary>
    public void ApplyManipulatedVariables(DateTime dTime)
    {
      if (dTime < nextSignalApply) return;
      nextSignalApply = dTime.AddSeconds(SIGNAL_UPDATE_SPAN);

      lock (communicator.BACnetDevice)
      {
        int iuNum = 0;
        for (int i = 0; i < vrfSystems.Length; i++)
        {
          ExVRFSystem vrf = vrfSystems[i];
          bool isSystemOn = false;
          for (int j = 0; j < vrf.VRFSystem.IndoorUnitNumber; j++)
          {
            BacnetObjectId boID;

            //On/off******************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_OUTPUT, (uint)getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.OnOff_Setting));
            bool isIUonSet = BACnetCommunicator.ConvertToBool(((BinaryOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.OnOff_Status));
            bool isIUonStt = BACnetCommunicator.ConvertToBool(((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            if (isIUonSet != isIUonStt) //設定!=状態の場合には更新処理
              ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = isIUonSet ? 1u : 0u;
            //1台でも室内機が動いていれば室外機はOn
            isSystemOn |= isIUonSet;

            //運転モード****************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT,
              (uint)getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.OperationMode_Setting));
            uint modeSet = ((MultiStateOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT,
              (uint)getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.OperationMode_Status));
            uint modeStt = ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            if (modeSet != modeStt) //設定!=状態の場合には更新処理
            {
              ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = modeSet;
              boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
                (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.ThermoOn_Status));
              //送風以外の場合にはサーモOn
              ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE =
                modeSet != 3 ? 1u : 0u;
            }

            vrf.IndoorUnitModes[j] =
              !isIUonSet ? ExVRFSystem.Mode.ShutOff :
              modeSet == 1 ? ExVRFSystem.Mode.Cooling :
              modeSet == 2 ? ExVRFSystem.Mode.Heating :
              modeSet == 3 ? ExVRFSystem.Mode.ThermoOff :
              modeSet == 4 ? ExVRFSystem.Mode.Auto : ExVRFSystem.Mode.Dry;

            //室内温度設定***************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, (uint)getInstanceNumber(ObjectNumber.AnalogValue, iuNum, MemberNumber.Setpoint));
            float tSp = ((AnalogValue<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            //ダイキンの設定温度は冷暖で5度の偏差を持つ
            vrf.SetSetpoint(tSp, j, true);
            vrf.SetSetpoint(tSp - 5, j, false);

            //フィルタ信号リセット********
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.FilterSignSignalReset));
            bool restFilter = BACnetCommunicator.ConvertToBool(((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            if (restFilter)
            {
              //リセット処理
              //***未実装***

              //信号を戻す
              ((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0;
            }

            //リモコン手元操作許可禁止*****
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_OnOff));
            bool rmtPmtOnOff = BACnetCommunicator.ConvertToBool(((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_OperationMode));
            bool rmtPmtMode = BACnetCommunicator.ConvertToBool(((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.RemoteControllerPermittion_Setpoint));
            bool rmtPmtSP = BACnetCommunicator.ConvertToBool(((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            vrf.PermitSPControl[j] = rmtPmtSP;
            //***未実装***

            //中央制御*******************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.CentralizedControl));
            bool cntCtrl = BACnetCommunicator.ConvertToBool(((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            //***未実装***

            //ファン風量*****************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.FanSpeed_Setting));
            uint fanSpdSet = ((MultiStateOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.FanSpeed_Status));
            uint fanSpdStt = ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            if (fanSpdSet != fanSpdStt)
              ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = fanSpdSet;

            double fRate =
              fanSpdSet == 1 ? 0.3 :
              fanSpdSet == 2 ? 1.0 : 0.7; //Low, High, Middleの係数は適当
            vrf.VRFSystem.SetIndoorUnitAirFlowRate(j, vrf.VRFSystem.IndoorUnits[j].NominalAirFlowRate * fRate);

            //風向***********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, (uint)getInstanceNumber(ObjectNumber.AnalogValue, iuNum, MemberNumber.AirflowDirection_Setting));
            uint afDirSet = (uint)((AnalogValue<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, (uint)getInstanceNumber(ObjectNumber.AnalogInput, iuNum, MemberNumber.AirflowDirection_Status));
            uint afDirStt = (uint)((AnalogInput<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            if (afDirSet != afDirStt) //設定!=状態の場合には更新処理
              ((AnalogInput<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = afDirSet;
            vrf.Direction[j] = Math.PI / 180d * afDirSet * 22.5;

            //強制サーモオフ*************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_OUTPUT, (uint)getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.ForcedThermoOff_Setting));
            bool fceTOffSet = BACnetCommunicator.ConvertToBool(((BinaryOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.ForcedThermoOff_Status));
            bool fceTOffStt = BACnetCommunicator.ConvertToBool(((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            if (fceTOffSet != fceTOffStt) //設定!=状態の場合には更新処理
              ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = fceTOffStt ? 1u : 0u;
            //***未実装***

            //省エネ指令*****************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_OUTPUT, (uint)getInstanceNumber(ObjectNumber.BinaryOutput, iuNum, MemberNumber.EnergySaving_Setting));
            bool engySavingSet = BACnetCommunicator.ConvertToBool(((BinaryOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT, (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.EnergySaving_Status));
            bool engySavingStt = BACnetCommunicator.ConvertToBool(((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE);
            if (engySavingSet != engySavingStt) //設定!=状態の場合には更新処理
              ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = engySavingSet ? 1u : 0u;
            //***未実装***

            //換気モード*****************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.VentilationMode_Setting));
            uint vModeSet = ((MultiStateOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.VentilationMode_Status));
            uint vModeStt = ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            if (vModeSet != vModeStt) //設定!=状態の場合には更新処理
              ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = vModeSet;
            //***未実装***

            //換気量********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateOutput, iuNum, MemberNumber.VentilationAmount_Setting));
            uint vAmountSet = ((MultiStateOutput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT, (uint)getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.VentilationAmount_Status));
            uint vAmountStt = ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            if (vAmountSet != vAmountStt) //設定!=状態の場合には更新処理
              ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = vAmountSet;
            //***未実装***

            //強制停止
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, (uint)getInstanceNumber(ObjectNumber.BinaryValue, iuNum, MemberNumber.ForcedSystemStop));
            uint fceStop = ((BinaryValue)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE;
            //***未実装***

            iuNum++;
          }
        }
      }
    }

    /// <summary>機器やセンサの検出値を取得する</summary>
    public void ReadMeasuredValues(DateTime dTime)
    {
      if (dTime < nextSignalRead) return;
      nextSignalRead = dTime.AddSeconds(SIGNAL_UPDATE_SPAN);

      lock (communicator.BACnetDevice)
      {
        int iuNum = 0;
        for (int i = 0; i < vrfSystems.Length; i++)
        {
          ExVRFSystem vrf = vrfSystems[i];
          for (int j = 0; j < vrf.VRFSystem.IndoorUnitNumber; j++)
          {
            BacnetObjectId boID;

            //警報***********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Alarm));
            //未実装
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u;

            //故障***********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT,
              (uint)getInstanceNumber(ObjectNumber.MultiStateInput, iuNum, MemberNumber.MalfunctionCode));
            //未実装
            ((MultiStateInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 1u;

            //室内温度設定***************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, (uint)getInstanceNumber(ObjectNumber.AnalogValue, iuNum, MemberNumber.Setpoint));
            ((AnalogValue<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE =
              (float)(vrf.VRFSystem.CurrentMode == Popolo.HVAC.MultiplePackagedHeatPump.VRFSystem.Mode.Heating ? vrf.GetSetpoint(j, false) + 5 : vrf.GetSetpoint(j, true));

            //フィルタサイン***************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.FilterSignSignal));
            //未実装
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u;

            //吸い込み室温****************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT,
              (uint)getInstanceNumber(ObjectNumber.AnalogInput, iuNum, MemberNumber.MeasuredRoomTemperature));
            ((AnalogInput<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = (float)vrf.VRFSystem.IndoorUnits[j].InletAirTemperature;

            //ガス消費（EHPのため0固定）****
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ACCUMULATOR,
              (uint)getInstanceNumber(ObjectNumber.Accumulator, iuNum, MemberNumber.AccumulatedGas));
            ((Accumulator<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0;

            //電力消費********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ACCUMULATOR,
              (uint)getInstanceNumber(ObjectNumber.Accumulator, iuNum, MemberNumber.AccumulatedPower));
            ((Accumulator<float>)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE += (float)vrf.VRFSystem.IndoorUnits[j].FanElectricity;

            //通信状況********************
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.CommunicationStatus));
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u; //1でBACnet通信エラー

            //圧縮機、ファン、ヒータ異常*****
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Compressor_Status));
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u; //1でエラー
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.IndoorFan_Status));
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u; //1でエラー
            boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_INPUT,
              (uint)getInstanceNumber(ObjectNumber.BinaryInput, iuNum, MemberNumber.Heater_Status));
            ((BinaryInput)communicator.BACnetDevice.FindBacnetObject(boID)).m_PROP_PRESENT_VALUE = 0u; //1でエラー

            iuNum++;
          }
        }
      }
    }

    /// <summary>BACnetControllerのサービスを開始する</summary>
    public void StartService()
    {
      communicator.StartService();
    }

    /// <summary>BACnetControllerのリソースを解放する</summary>
    public void EndService()
    {
      communicator.EndService();
    }


    #endregion

    #region 構造体定義

    /// <summary>室外機と室内機の番号を保持する</summary>
    private struct VRFUnitIndex
    {
      public string ToString()
      {
        return (OUnitIndex + 1).ToString() + "-" + (IUnitIndex + 1).ToString();
      }

      public int OUnitIndex { get; private set; }

      public int IUnitIndex { get; private set; }

      public VRFUnitIndex(int oUnitIndex, int iUnitIndex)
      {
        OUnitIndex = oUnitIndex;
        IUnitIndex = iUnitIndex;
      }
    }

    #endregion

  }
}
