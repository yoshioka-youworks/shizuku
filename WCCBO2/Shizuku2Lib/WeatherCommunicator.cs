﻿using System.IO.BACnet;
using BaCSharp;

namespace Shizuku2.BACnet
{

  /// <summary>Shizuku2のWeatherモニタとの通信ユーティリティクラス</summary>
  public class WeatherCommunicator
  {

    #region 定数宣言

    /// <summary>Device ID</summary>
    public const uint WEATHERMONITOR_DEVICE_ID = 4;

    /// <summary>排他的ポート番号</summary>
    public const int WEATHERMONITOR_EXCLUSIVE_PORT = 0xBAC0 + (int)WEATHERMONITOR_DEVICE_ID;

    /// <summary>WeatherモニタのBACnetアドレス</summary>
    private readonly BacnetAddress bacAddress;

    #endregion

    #region 列挙型

    /// <summary>項目</summary>
    public enum WeatherMonitorMember
    {
      /// <summary>乾球温度</summary>
      DrybulbTemperature = 1,
      /// <summary>相対湿度</summary>
      RelativeHumdity = 2,
      /// <summary>水平面全天日射</summary>
      GlobalHorizontalRadiation = 3,
    }

    #endregion

    #region インスタンス変数・プロパティ

    /// <summary>BACnet通信用オブジェクト</summary>
    private BACnetCommunicator communicator;

    #endregion

    #region コンストラクタ

    /// <summary>インスタンスを初期化する</summary>
    /// <param name="id">通信に使うBACnet DeviceのID</param>
    /// <param name="name">通信に使うBACnet Deviceの名前</param>
    /// <param name="description">通信に使うBACnet Deviceの説明</param>
    /// <param name="ipAddress">WeatherモニタのIPアドレス（「xxx.xxx.xxx.xxx」の形式）</param>
    public WeatherCommunicator(uint id, string name, string description, string ipAddress = "127.0.0.1")
    {
      DeviceObject dObject = new DeviceObject(id, name, description, true);
      communicator = new BACnetCommunicator(dObject, (int)(0xBAC0 + id));
      bacAddress = new BacnetAddress(BacnetAddressTypes.IP, ipAddress + ":" + WEATHERMONITOR_EXCLUSIVE_PORT.ToString());
      communicator.StartService();

      //Who is送信
      communicator.Client.WhoIs();
    }

    #endregion

    #region インスタンスメソッド

    /// <summary>乾球温度[C]を取得する</summary>
    /// <param name="succeeded">通信が成功したか否か</param>
    /// <returns>乾球温度[C]</returns>
    public double GetDrybulbTemperature(out bool succeeded)
    {
      BacnetObjectId boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, (uint)WeatherMonitorMember.DrybulbTemperature);

      if (communicator.Client.ReadPropertyRequest(bacAddress, boID, BacnetPropertyIds.PROP_PRESENT_VALUE, out IList<BacnetValue> val))
      {
        succeeded = true;
        return (double)val[0].Value;
      }

      succeeded = false;
      return 0;
    }

    /// <summary>相対湿度[%]を取得する</summary>
    /// <param name="succeeded">通信が成功したか否か</param>
    /// <returns>相対湿度[%]</returns>
    public double GetRelativeHumidity(out bool succeeded)
    {
      BacnetObjectId boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, (uint)WeatherMonitorMember.RelativeHumdity);

      if (communicator.Client.ReadPropertyRequest(bacAddress, boID, BacnetPropertyIds.PROP_PRESENT_VALUE, out IList<BacnetValue> val))
      {
        succeeded = true;
        return (double)val[0].Value;
      }

      succeeded = false;
      return 0;
    }

    /// <summary>水平面全天日射[W/m2]を取得する</summary>
    /// <param name="succeeded">通信が成功したか否か</param>
    /// <returns>水平面全天日射[W/m2]</returns>
    public double GetGlobalHorizontalRadiation(out bool succeeded)
    {
      BacnetObjectId boID = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, (uint)WeatherMonitorMember.GlobalHorizontalRadiation);

      if (communicator.Client.ReadPropertyRequest(bacAddress, boID, BacnetPropertyIds.PROP_PRESENT_VALUE, out IList<BacnetValue> val))
      {
        succeeded = true;
        return (double)val[0].Value;
      }

      succeeded = false;
      return 0;
    }

    #endregion

  }
}
