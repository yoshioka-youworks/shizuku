import threading
import time
import datetime

# from datetime import datetime, timedelta
from bacpypes.core import run, deferred
from bacpypes.pdu import Address, GlobalBroadcast
from bacpypes.apdu import ReadPropertyRequest, WritePropertyRequest, SubscribeCOVPropertyRequest
from bacpypes.app import BIPSimpleApplication
from bacpypes.local.device import LocalDeviceObject
from bacpypes.primitivedata import ObjectIdentifier, Enumerated, Real, Integer, BitString, Boolean, Unsigned
from bacpypes.object import get_datatype, AnalogOutputObject
from bacpypes.iocb import IOCB
from bacpypes.basetypes import DateTime, Date, Time, PropertyReference
from bacpypes.constructeddata import Any

from bacpypes.core import enable_sleeping

class BACnetCommunicator(BIPSimpleApplication):
    """BACnet通信用クラス
    """  

    def __init__(self, id, name, time_out_sec = 1.0):
        """インスタンスを初期化する

        Args:
            id (int): 通信に使うDeviceのID
            name (str): 通信に使うDeviceの名前
        """

        # タイムアウトまでの時間
        self.time_out = time_out_sec

        # idを保存
        self.id = id

        # DateTimeのCOV登録状況
        self.dtcov_scribed = False
        self.acc_rate = 0
        self.base_real_datetime = datetime.datetime.today()
        self.base_sim_datetime = datetime.datetime.today()
            
        this_device = LocalDeviceObject(
            objectName=name,
            objectIdentifier=id,
            maxApduLengthAccepted=1024,
            segmentationSupported='segmentedBoth',
            vendorIdentifier=15,
            )

        # 別スレッドでBACnet通信を処理
        self.core = threading.Thread(target = run, daemon=True)
        self.core.start()

        # BACnetコントローラを用意して起動まで待機
        BIPSimpleApplication.__init__(self, this_device, '127.0.0.1:' + str(0xBAC0 + id))
        time.sleep(1)

        # idが0 (47808)以外だとWhoisが効かない。修正必要。
        self.who_is()

        # 具体的な処理はわからんが、これでiocb.waite()が高速化する
        # おそらく通信処理の合間に待機する処理が有効になるのだろう
        enable_sleeping()

    def read_present_value(self, addr, obj_id, data_type):
        """Read property requestでPresent valueを読み取る（同期処理）

        Args:
            addr (string): 通信先のBACnet Deviceのアドレス（xxx.xxx.xxx.xxx:port）
            obj_id (string): 通信先のBACnet DeviceのオブジェクトID
            data_type (Union[Real,Boolean,Integer,DateTime,str]): データの種別(bacpypes.primitivedata)

        Returns:
            list: 読み取り成功の真偽, Present value
        """

        request = self._make_request(addr, obj_id, True)

        iocb = IOCB(request)
        iocb.set_timeout(self.time_out, err=TimeoutError)
        deferred(self.request_io, iocb)

        # 通信完了まで待機
        iocb.wait()

        # 通信失敗
        if iocb.ioError:
            return False, str(iocb.ioError)

        # 通信成功
        elif iocb.ioResponse:
            apdu = iocb.ioResponse
            # 型変換して出力
            val = apdu.propertyValue.cast_out(data_type)
            if (isinstance(val, DateTime)):
                return True, datetime.datetime(
                    year=1900 + val.date[0],
                    month=val.date[1],
                    day=val.date[2],
                    hour=val.time[0],
                    minute=val.time[1],
                    second=val.time[2])
            else:
                return True, val
        
    def read_present_value_async(self, addr, obj_id, data_type, call_back_fnc):
        """Read property requestでPresent valueを読み取る（非同期処理）

        Args:
            addr (string): 通信先のBACnet Deviceのアドレス（xxx.xxx.xxx.xxx:port）
            obj_id (string): 通信先のBACnet DeviceのオブジェクトID
            data_type (bacpypes.primitivedata): データの種別
            call_back_fnc (function): 通信終了時のコールバック関数。引数は以下の通り。
                str:通信先のBACnet Deviceのアドレス,
                str:通信先のBACnet DeviceのオブジェクトID, 
                bool:読み取り成功の真偽,
                Union[Real,Boolean,Integer,DateTime,str]:Present valueまたは読み取り失敗時の文字列
        """        
        request = self._make_request(addr, obj_id, True)
 
        iocb = IOCB(request)
        iocb.add_callback(self._complete_read_present_value_async, data_type, addr, obj_id, call_back_fnc)
        
        deferred(self.request_io, iocb)
        
    def _complete_read_present_value_async(self, iocb, data_type, addr, obj_id, call_back_fnc):
        if(call_back_fnc == None):
            return
        
        if iocb.ioResponse:
            apdu = iocb.ioResponse
            call_back_fnc(addr, obj_id, True, apdu.propertyValue.cast_out(data_type))
            return

        if iocb.ioError:
            call_back_fnc(addr, obj_id, False, str(iocb.ioError))
            return
        
    def write_present_value(self, addr, obj_id, value):
        """Write property requestでPresent valueを書き込む（同期処理）

        Args:
            addr (string): 通信先のBACnet Deviceのアドレス（xxx.xxx.xxx.xxx:port）
            obj_id (string): 通信先のBACnet DeviceのオブジェクトID
            value (Union[Real,Boolean,Integer,DateTime]): Present value

        Returns:
            bool: 書き込み成功の真偽
        """        
        request = self._make_request(addr, obj_id, False)
        request.propertyValue.cast_in(value)

        iocb = IOCB(request)
        iocb.set_timeout(self.time_out, err=TimeoutError)
        deferred(self.request_io, iocb)

        # 通信完了まで待機
        iocb.wait()

        # 通信失敗
        if iocb.ioError:
            return False, str(iocb.ioError)

        # 通信成功
        elif iocb.ioResponse:
            return True, str(value)
        
    def write_present_value_async(self, addr, obj_id, value, call_back_fnc):
        """Write property requestでPresent valueを書き込む（非同期処理）

        Args:
            addr (string): 通信先のBACnet Deviceのアドレス（xxx.xxx.xxx.xxx:port）
            obj_id (string): 通信先のBACnet DeviceのオブジェクトID
            value (Union[Real,Boolean,Integer,DateTime]): Present value
            call_back_fnc (function): 通信終了時のコールバック関数。引数は以下の通り。
                str:通信先のBACnet Deviceのアドレス,
                str:通信先のBACnet DeviceのオブジェクトID, 
                bool:書き込み成功の真偽,
                str:書き込み失敗時のエラー文
        """        
        request = self._make_request(addr, obj_id, False)        
        request.propertyValue.cast_in(value)

        iocb = IOCB(request)
        iocb.add_callback(self._complete_write_present_value_async, addr, obj_id, call_back_fnc)
        deferred(self.request_io, iocb)
        
    def _complete_write_present_value_async(self, iocb, addr, obj_id, call_back_fnc):
        if(call_back_fnc == None) :
            return
        
        if iocb.ioResponse:
            call_back_fnc(addr, obj_id, True, None)
            return

        if iocb.ioError:
            call_back_fnc(addr, obj_id, False, str(iocb.ioError))
            return

    def _make_request(self, addr, obj_id, is_read):
        prop_id = 'presentValue'
        obj_id = ObjectIdentifier(obj_id).value
        if prop_id.isdigit():
            prop_id = int(prop_id)

        datatype = get_datatype(obj_id[0], prop_id)
        if not datatype:
            raise ValueError("invalid property for object type")
            
        if(is_read):
            return ReadPropertyRequest(
                destination=Address(addr),
                objectIdentifier=obj_id, 
                propertyIdentifier=prop_id
                )
        else:
            return WritePropertyRequest(
                destination=Address(addr),
                objectIdentifier=obj_id, 
                propertyIdentifier=prop_id,
                propertyValue = Any(),
                )

    def subscribe_date_time_cov(self, monitored_ip):
        # 既に登録されている場合には二重登録を回避
        if self.dtcov_scribed:
            return
                        
        request = SubscribeCOVPropertyRequest(
            subscriberProcessIdentifier=self.id,
            monitoredObjectIdentifier=("analogOutput",2), # 加速度
            monitoredPropertyIdentifier=PropertyReference(propertyIdentifier='presentValue'),
            covIncrement=0,
        )
        request.pduDestination = Address(monitored_ip)
        self.mon_id = monitored_ip # 監視IPアドレスを保存

        iocb = IOCB(request)
        # iocb.set_timeout(self.time_out, err=TimeoutError)
        deferred(self.request_io, iocb)

        # 通信完了まで待機
        iocb.wait()

        # 通信失敗
        if iocb.ioError:
            return False

        # 通信成功
        elif iocb.ioResponse:
            self.dtcov_scribed = True
            self._update_date_time(monitored_ip)
            return True

    def do_ConfirmedCOVNotificationRequest(self, apdu):
        print('receieve ConfirmedCOVNotificationRequest')

    def do_UnconfirmedCOVNotificationRequest(self, apdu):
        if(
            apdu.pduSource == self.mon_id and
            apdu.monitoredObjectIdentifier == ("analogOutput",2) and
            apdu.listOfValues[0].propertyIdentifier == 'presentValue'):
                # 別スレッドで日時を更新
                thread = threading.Thread(target=self._update_date_time, args=(self.mon_id,))
                thread.start()

    def _update_date_time(self, dt_ip_address):
        val = self.read_present_value(dt_ip_address, 'analogOutput:2', Integer)
        self.acc_rate = val[1] if val[0] else 0
        time.sleep(0.1) #これがないとiocbの返り値が別スレッドの値になることがある。本質的な回避策ではないので問題有り
        val = self.read_present_value(dt_ip_address, 'datetimeValue:3', DateTime)
        self.base_real_datetime = val[1] if val[0] else val[1] #None
        time.sleep(0.1) #これがないとiocbの返り値が別スレッドの値になることがある。本質的な回避策ではないので問題有り
        val = self.read_present_value(dt_ip_address, 'datetimeValue:4', DateTime)
        self.base_sim_datetime = val[1] if val[0] else val[1] #None

    def current_date_time(self):
        return (datetime.datetime.today() - self.base_real_datetime) * self.acc_rate + self.base_sim_datetime

def request(self, apdu):
    BIPSimpleApplication.request(self, apdu)

def indication(self, apdu):
    BIPSimpleApplication.indication(self, apdu)

def response(self, apdu):
    BIPSimpleApplication.response(self, apdu)

def confirmation(self, apdu):
    BIPSimpleApplication.confirmation(self, apdu)

# region サンプル

def main():
    master = BACnetCommunicator(999, 'myDevice')

    # Who is送信
    master.who_is()

    # 日時のCOVを登録
    print('Subscribe COV: ' + str(master.subscribe_date_time_cov('127.0.0.1:47809')))    

    # 同期でread property
    pValue = master.read_present_value('127.0.0.1:47817', 'analogValue:1', Integer)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'analogOutput:2', Integer)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'analogInput:3', Integer)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'analogValue:4', Real)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'analogOutput:5', Real)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'analogInput:6', Real)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'binaryValue:7', Enumerated)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'binaryOutput:8', Enumerated)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'binaryInput:9', Enumerated)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'multiStateValue:10', Unsigned)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'multiStateOutput:11', Unsigned)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'multiStateInput:12', Unsigned)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))
    pValue = master.read_present_value('127.0.0.1:47817', 'datetimeValue:13', DateTime)
    print('synchronously reading ' + ('success, value=' + str(pValue[1]) if pValue[0] else ('failed because of ' + pValue[1])))

    #同期でwrite property
    success = master.write_present_value('127.0.0.1:47817', 'analogValue:1', Integer(3))
    print('synchronously writing analogValue(int) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'analogOutput:2', Integer(2))
    print('synchronously writing analogOutput(int) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'analogInput:3', Integer(1))
    print('synchronously writing analogInput(int) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'analogValue:4', Real(3))
    print('synchronously writing analogValue(real) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'analogOutput:5', Real(2))
    print('synchronously writing analogOutput(real) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'analogInput:6', Real(1))
    print('synchronously writing analogInput(real) ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'binaryValue:7', Enumerated(1))
    print('synchronously writing binaryValue ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'binaryOutput:8', Enumerated(1))
    print('synchronously writing binaryOutput ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'binaryInput:9', Enumerated(1))
    print('synchronously writing binaryInput ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'multiStateValue:10', Unsigned(3))
    print('synchronously writing multiStateValue ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'multiStateOutput:11', Unsigned(2))
    print('synchronously writing multiStateOutput ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'multiStateInput:12', Unsigned(1))
    print('synchronously writing multiStateInput ' + ('success' if success[0] else ('failed because of ' + success[1])))
    success = master.write_present_value('127.0.0.1:47817', 'datetimeValue:13', DateTime(date=Date().now().value, time=Time().now().value))
    print('synchronously writing dateTimeValue ' + ('success' if success[0] else ('failed because of ' + success[1])))

    # 非同期でwrite property
    master.read_present_value_async('127.0.0.1:47817', 'analogValue:1', Integer, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'analogOutput:2', Integer, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'analogInput:3', Integer, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'analogValue:4', Real, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'analogOutput:5', Real, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'analogInput:6', Real, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'binaryValue:7', Enumerated, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'binaryOutput:8', Enumerated, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'binaryInput:9', Enumerated, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'multiStateValue:10', Unsigned, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'multiStateOutput:11', Unsigned, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'multiStateInput:12', Unsigned, my_call_back_write)
    master.read_present_value_async('127.0.0.1:47817', 'datetimeValue:13', DateTime, my_call_back_write)

    # 非同期でwrite property
    master.write_present_value_async('127.0.0.1:47817', 'analogValue:1', Integer(3), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'analogOutput:2', Integer(2), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'analogInput:3', Integer(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'analogValue:4', Real(3), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'analogOutput:5', Real(2), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'analogInput:6', Real(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'binaryValue:7', Enumerated(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'binaryOutput:8', Enumerated(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'binaryInput:9', Enumerated(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'multiStateValue:10', Unsigned(3), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'multiStateOutput:11', Unsigned(2), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'multiStateInput:12', Unsigned(1), my_call_back_write)
    master.write_present_value_async('127.0.0.1:47817', 'datetimeValue:13', DateTime(date=Date().now().value, time=Time().now().value), my_call_back_write)
 
    # 無限ループで日時を表示
    while True:
        print(master.current_date_time().strftime('%Y/%m/%d %H:%M:%S'))
        time.sleep(0.2)
        pass

def my_call_back_read(addr, obj_id, success, value):
    if(success):
        print('success asynchronously reading ' + str(addr) + ' : ' + str(obj_id) + ', value=' + str(value))
    else:
        print('failed asynchronously reading ' + str(addr) + ' : ' + str(obj_id) + ', ' + str(value))
    return

def my_call_back_write(addr, obj_id, success, value):
    if(success):
        print('success asynchronously writing ' + str(addr) + ' : ' + str(obj_id))
    else:
        print('failed asynchronously writing ' + str(addr) + ' : ' + str(obj_id) + ', ' + str(value))
    return

if __name__ == "__main__":
    main()

# endregion