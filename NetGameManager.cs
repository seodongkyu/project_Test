using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net.Sockets;
using DefineServerUtility;
using DefineGameUtil;


public struct stRoomInfo
{
    public long _oppID;
    public long _oppAvatarID;
    public string _oppNick;
    public bool _isReady;
    public bool _isMatser;
    public stRoomInfo(long id, long avatar,string nick, bool ready,bool master)
    {
        _oppID = id;
        _oppAvatarID = avatar;
        _oppNick = nick;
        _isReady = ready;
        _isMatser = master;
    }
}

public class NetGameManager : TSingleton<NetGameManager>
{
    const string _ip = "127.0.0.1";
    const short _port = 80;

    Socket _sock;
    Queue<Packet> _sendQ = new Queue<Packet>();
    Queue<Packet> _recvQ = new Queue<Packet>();

    bool _isConnectFailed = false;
    int _retryCount = 3;

    stRoomInfo _room = new stRoomInfo(0, 0,string.Empty, false,false);
    public stRoomInfo _roomInfo { get { return _room; } }

    RoomUIWindow _roomUIWindow;
    protected override void Init()
    {
        base.Init();
    }

    public void NetConnect()
    {
        StartCoroutine(Connectings(_ip, _port));
    }

    IEnumerator Connectings(string ipAddr, short port)
    {
        int cnt = 0;
        while(true)
        {
            yield return new WaitForSeconds(1);
            try
            {
                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _sock.Connect(ipAddr, port);
                StartCoroutine(ReceiveProcess());
                StartCoroutine(SendProcess());
                SceneControlManager._instance.StartScene((int)eSceneType.Login);
                break;
            }
            catch
            {
                //  서버와 접속이  되지 않습니다....메세지 출력.
                cnt++;
                if (cnt > _retryCount)
                {
                    _isConnectFailed = true;
                    break;
                }
            }
            yield return new WaitForSeconds(3);
        }
    }

    private void Update()
    {
        if (_sock != null && _sock.Connected)
        {
            if (_sock.Poll(0, SelectMode.SelectRead))
            {
                byte[] buffer = new byte[ConvertPacketFunc._maxByte];

                int rLen = _sock.Receive(buffer);
                if (rLen > 0)
                {
                    Packet pack = (Packet)ConvertPacketFunc.ByteArrayToStructure(buffer, typeof(Packet), buffer.Length);
                    _recvQ.Enqueue(pack);
                }
                //  큐를 쓰고 uuid 받아오기
            }
        }
        else
        {
            //  로딩 프로세스.
            if (_isConnectFailed)
            {
                //  메세지창을 띄우고 메세지창을 클릭하면 앱 종료.
                Debug.Log("서버가...");
            }
            else
            {
                //  로딩이 계속 돈다.
            }
        }
    }

    IEnumerator SendProcess()
    {
        while (true)
        {
            if (_sendQ.Count > 0)
            {
                Packet pack = _sendQ.Dequeue();
                byte[] buffer = ConvertPacketFunc.StructureToByteArray(pack);
                _sock.Send(buffer);
            }
            yield return null;
        }
    }

    IEnumerator ReceiveProcess()
    {
        while (true)
        {
            if (_recvQ.Count > 0)
            {
                Packet pack = _recvQ.Dequeue();
                switch ((eReceiveMessage)pack._protocolID)
                {
                    case eReceiveMessage.GivingUUID:
                        Receive_GivingUUID(pack);
                        break;
                    case eReceiveMessage.Account_Sucess:
                        Receive_LoginSucess recv_Account = 
                            (Receive_LoginSucess)ConvertPacketFunc.ByteArrayToStructure(pack._datas, typeof(Receive_LoginSucess), pack._totalSize);
                        UserInfos._instance.SetIDPW(recv_Account._id, recv_Account._pw);

                        UILoginWindow loginWnd = GameObject.FindGameObjectWithTag("LoginUIMain").GetComponent<UILoginWindow>();
                        loginWnd.InstantiateInfoSelectWndObj();
                        break;
                    case eReceiveMessage.Account_Failed:
                        Debug.Log("로그인 실패");
                        break;
                    case eReceiveMessage.Check_Duplication_Sucess:
                    case eReceiveMessage.Check_Duplication_Failed:
                        Receive_Duplicate_Result nickResult =
                            (Receive_Duplicate_Result)ConvertPacketFunc.ByteArrayToStructure(pack._datas, typeof(Receive_Duplicate_Result), pack._totalSize);
                        InfoSelectWindow infoWnd = GameObject.FindGameObjectWithTag("InfoSelectWindow").GetComponent<InfoSelectWindow>();

                        if ((eReceiveMessage)pack._protocolID == eReceiveMessage.Check_Duplication_Sucess)
                        {
                            string sucess = string.Format("{0}  사용가능한 이름입니다.", nickResult._nick);
                            infoWnd.Set_GuideMessage(sucess, Color.green, nickResult._nick);
                        }
                        else
                        {
                            string failed = string.Format("{0}  사용할 수 없는 이름입니다.", nickResult._nick);
                            infoWnd.Set_GuideMessage(failed, Color.red);
                        }
                        break;
                    case eReceiveMessage.SetUp_UserInfoComplete:
                        //  최정결정 성공
                        Receive_Definite_Infomation recv_definite =
                            (Receive_Definite_Infomation)ConvertPacketFunc.ByteArrayToStructure(pack._datas, typeof(Receive_Definite_Infomation), pack._totalSize);
                        UserInfos._instance.BeginGameSet(recv_definite._nick, recv_definite._avatarID);
                        SceneControlManager._instance.StartScene((int)eSceneType.Room);
                        break;
                    case eReceiveMessage.EntryRoom:
                        Receive_EntryRoom(pack);
                        break;
                    case eReceiveMessage.Click_Ready:
                        Receive_ReadyType(pack);
                        break;
                    case eReceiveMessage.Click_Start:
                        SceneControlManager._instance.StartScene((int)eSceneType.Ingame);
                        break;
                }
            }
            yield return null;
        }
    }
    #region [SendProcessing Func]
    public void Send_SelectAccount(string id, string pw)
    {
        Send_SelectAccountGiven AccountGiven;
        AccountGiven._UUID = UserInfos._instance._myUUID;
        AccountGiven._id = id;
        AccountGiven._pw = pw;
        byte[] data = ConvertPacketFunc.StructureToByteArray(AccountGiven);
        Packet send = ConvertPacketFunc.CreatePack((int)eSendMessage.Given_SelectAccount, UserInfos._instance._myUUID, data.Length, data);

        _sendQ.Enqueue(send);
    }
    public void Send_CheckDuplicate(string nickName)
    {
        Send_SelectNickName nickNameGiven;
        nickNameGiven._nick = nickName;
        byte[] data = ConvertPacketFunc.StructureToByteArray(nickNameGiven);
        Packet send = ConvertPacketFunc.CreatePack((int)eSendMessage.Check_Duplicates, UserInfos._instance._myUUID, data.Length, data);

        _sendQ.Enqueue(send);
    }
    //  아바타와 닉네임을 확정하여 서버에 보내는 함수
    public void Send_Definite_Infomation(int avatarID, string nickName)
    {
        Send_Definite_Information definiteInfo;
        definiteInfo._avatarID = avatarID;
        definiteInfo._nick = nickName;
        byte[] data = ConvertPacketFunc.StructureToByteArray(definiteInfo);
        Packet send = ConvertPacketFunc.CreatePack((int)eSendMessage.Definite_Information, UserInfos._instance._myUUID, data.Length, data);

        _sendQ.Enqueue(send);
    }
    public void Send_EntryMyInfo()
    {
        //  내가  들어왔다는 것만 알려주면 됨.
        Send_EntryInfomation send_Entry;
        send_Entry._ID = UserInfos._instance._myUUID;
        send_Entry._AvatarID = UserInfos._instance._myAvatarID;
        send_Entry._Nick = UserInfos._instance._myNick;
        send_Entry._isMaster = _room._isMatser;

        byte[] data = ConvertPacketFunc.StructureToByteArray(send_Entry);

        Packet send = ConvertPacketFunc.CreatePack((int)eSendMessage.EntryRoom, UserInfos._instance._myUUID, data.Length, data);
        _sendQ.Enqueue(send);
    }
    public void Send_ClickStartButton()
    {
        Packet send;
        if (_room._isMatser)
        {
            //  방장이라면
            if (_room._isReady)
            {
                //  게임시작 (씬 전환)
                //  서버에 신호 전송
                send = ConvertPacketFunc.CreatePack((int)eSendMessage.Click_Start, UserInfos._instance._myUUID, 0, null);
                _sendQ.Enqueue(send);
                SceneControlManager._instance.StartScene((int)eSceneType.Ingame);
                return;
            }
        }
        else
        {
            //  방장이 아니라면
            Send_Ready send_ready;

            if (_room._isReady)
                send_ready._isReady = false;
            else
                send_ready._isReady = true;

            byte[] data = ConvertPacketFunc.StructureToByteArray(send_ready);
            send = ConvertPacketFunc.CreatePack((int)eSendMessage.Click_Ready, UserInfos._instance._myUUID, data.Length, data);
            _sendQ.Enqueue(send);
        }
    }
    #endregion [SendProcessing Func]


    #region [recvProcessing Func]
    void Receive_GivingUUID(Packet recv)
    {
        Receive_GivingUUID give = (Receive_GivingUUID)ConvertPacketFunc.ByteArrayToStructure(recv._datas, typeof(Receive_GivingUUID), recv._totalSize);
        UserInfos._instance.SetUUID(give._UUID);
    }
    void Receive_EntryRoom(Packet recv)
    {
        //  상대의 정보를 받아온다.
        //  방장인지 아닌지 체크는?
        Receive_EntryInfomation recv_entry =
            (Receive_EntryInfomation)ConvertPacketFunc.ByteArrayToStructure(recv._datas, typeof(Receive_EntryInfomation), recv._totalSize);

        if (recv_entry._isMaster)
        {
            _room._isMatser = true;
            if (_roomUIWindow == null)
                _roomUIWindow = GameObject.FindGameObjectWithTag("RoomUIWindow").GetComponent<RoomUIWindow>();
            _roomUIWindow.ChangeReadyBtnToStartBtn();
        }
            

        _room._oppID = recv_entry._ID;
        _room._oppAvatarID = recv_entry._AvatarID;
        _room._oppNick = recv_entry._Nick;

        if(_room._oppID != 0)
        {
            if(_roomUIWindow == null)
                _roomUIWindow = GameObject.FindGameObjectWithTag("RoomUIWindow").GetComponent<RoomUIWindow>();
            _roomUIWindow.SetOpponentInfo();
        }
    }
    void Receive_ReadyType(Packet recv)
    {
        Receive_Ready recv_ready =
            (Receive_Ready)ConvertPacketFunc.ByteArrayToStructure(recv._datas, typeof(Receive_Ready), recv._totalSize);

        _room._isReady = recv_ready._isReady;
    }
    #endregion [recvProcessing Func]

}
