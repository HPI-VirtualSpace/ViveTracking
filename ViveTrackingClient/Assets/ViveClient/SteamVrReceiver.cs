using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Assets.ViveClient
{
    [RequireComponent(typeof(AsyncTcpSocket))]
    public class SteamVrReceiver : MonoBehaviour
    {
        [Header("Server IP")]
        public string MappingServerIp = "192.168.1.116";
        public string MulticastAddress = "226.0.0.1";

        [Header("Tracking Properties")]
        public int PortTrackingData = 33000;
        public string SocketBufferSize = "0x100000";
        public int DataBufferSize = 20;

        [Header("Mapping Properties")]
        public int PortMappingData = 11000;
        public char ServerMessageEntriesSeparator = ';';
        public char ServerMessageEntryValueSeparator = '-';
        public string RequestForNewDictionary = "REQUIRETABLE";

        public delegate void NewTrackingDataAction();
        public static event NewTrackingDataAction OnNewTrackingData;

        private static char _serverMessageEntriesSeparator;
        private static char _serverMessageEntryValueSeparator;
        private static Dictionary<int, string> _id2name;

        private float _last;
        private int _socketBufsize = 0x100000;
        private Socket _socketTracking;
        private byte[] _bufferTracking;
        private Socket _socketMapping;
        private byte[] _bufferMapping;
        private Dictionary<string, GameObject> gameObjectDictionary = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> createdObjectDictionary;
        private TrackedObjectData[][] _dataBuffer;
        private int _dataBufferHead;
        private int _receivedPackages;
        private AsyncTcpSocket _asyncTcpSocket;
        private Thread _udpThread;
        private static bool _checkCreatedObjects;
        private bool _threadRunning;
        private bool _sendRequest;
        private int _lastHead;
        private List<float> _receivedPackagesTimes = new List<float>();

        private static readonly object syncLock = new object();
        private static string RequestString;

        struct TrackedObjectData
        {
            public int id;
            public Vector3 pos;
            public Quaternion rot;
        }

        // Use this for initialization
        void Awake()
        {
            //set up udp for tracking
            _socketTracking = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socketTracking.Bind(new IPEndPoint(IPAddress.Any, PortTrackingData));
            IPAddress mulIP = IPAddress.Parse(MulticastAddress);
            _socketTracking.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(mulIP,IPAddress.Any));
            _socketTracking.Blocking = false;
            _socketBufsize = (int)new System.ComponentModel.Int32Converter().ConvertFromString(SocketBufferSize);
            _socketTracking.ReceiveBufferSize = _socketBufsize;
            _bufferTracking = new byte[_socketBufsize];
            _dataBuffer = new TrackedObjectData[DataBufferSize][];
            _dataBufferHead = -1;

            //set up tcp for mapping
            RequestString = RequestForNewDictionary;
            _serverMessageEntriesSeparator = ServerMessageEntriesSeparator;
            _serverMessageEntryValueSeparator = ServerMessageEntryValueSeparator;
            _id2name = new Dictionary<int, string>();
            _asyncTcpSocket = gameObject.GetComponent<AsyncTcpSocket>();
            _asyncTcpSocket.Port = PortMappingData;
            _asyncTcpSocket.ServerIp = MappingServerIp;
            AsyncTcpSocket.OnReceiveMessage += DeserializeTcpMessage;

            gameObjectDictionary = new Dictionary<string, GameObject>();
            createdObjectDictionary = new Dictionary<string, GameObject>();
            _receivedPackagesTimes = new List<float>();

            StartReceiveThread();
        }

        private static void DeserializeTcpMessage(string msg)
        {
            var newDictionary = new Dictionary<int, string>();
            msg = msg.Remove(0, RequestString.Length);
            var split = msg.Split(_serverMessageEntriesSeparator);
            foreach (var splitString in split)
            {
                var splitAgain = splitString.Split(_serverMessageEntryValueSeparator);
                int key;
                if (splitAgain.Length != 2 || !int.TryParse(splitAgain[0], out key)) continue;
                var value = splitAgain[1];
                newDictionary.Add(key, value);
            }
            _id2name = newDictionary;
            _checkCreatedObjects = true;
        }

        private static string SerializeTcpMessage(Dictionary<int, string> dictionary)
        {
            var resultString = "";
            var stringList = dictionary.Select(d => d.Key + "" + _serverMessageEntryValueSeparator + "" + d.Value).ToList();
            for (var s = 0; s < stringList.Count; s++)
            {
                resultString += stringList[s];
                if (s < stringList.Count - 1)
                    resultString += _serverMessageEntriesSeparator;
            }
            return resultString;
        }

        private void StartReceiveThread()
        {
            try
            {
                _threadRunning = true;
                _udpThread = new Thread(ReceiveLoop)
                {
                    Priority = System.Threading.ThreadPriority.Highest
                };
                _udpThread.Start();
            }
            catch (Exception ex)
            {
                Debug.Log("error starting thread: " + ex.Message);
            }
        }

        private void StopReceiveLoop()
        {
            _threadRunning = false;

            Thread.Sleep(100);
            if (_udpThread != null)
            {
                _udpThread.Abort();
                // serialThread.Join();
                Thread.Sleep(100);
                _udpThread = null;
            }
        }

        private void ReceiveRead()
        {
            try
            {
                int bytesReceived = _socketTracking.Receive(_bufferTracking);
                //Debug.Log("bytes " + bytesReceived);
                if (bytesReceived == 0) return;
                var msg = new byte[bytesReceived];
                Array.Copy(_bufferTracking, msg, bytesReceived);
                lock (syncLock)
                {
                    if (++_dataBufferHead >= DataBufferSize)
                        _dataBufferHead = 0;
                    _receivedPackages++;
                    _dataBuffer[_dataBufferHead] = FromByteArray<TrackedObjectData>(msg);
                    //Debug.Log("package received " + _dataBufferHead);
                }
            }
            catch 
            {
                //Debug.LogError(e.Message);
            }
        }

        private void ReceiveLoop()
        {
            while (_threadRunning)
                ReceiveRead();
        }

        public int GetPps()
        {
            return _receivedPackagesTimes.Count;
        }

        public void Quit()
        {
            if (_threadRunning)
                StopReceiveLoop();
            if (_socketTracking != null)
                _socketTracking.Close();
        }

        private void OnApplicationQuit()
        {
            Quit();
        }

        private float _lastSent;
        void Update()
        {
            //ReceiveRead();
            
            if (_sendRequest && AsyncTcpSocket.IsConnected() && Time.unscaledTime - _lastSent > 1f)
            {
                _lastSent = Time.unscaledTime;
                _sendRequest = false;
                AsyncTcpSocket.Send(RequestForNewDictionary);
            }
            if (_checkCreatedObjects)
            {
                CheckCreatedObjects();
                _checkCreatedObjects = false;
            }
            TrackedObjectData[] lastTrackedData;
            if (GetLastTrackingData(out lastTrackedData))
            {
                //Debug.Log("package time : " + (Time.time - _last));
                _last = Time.unscaledTime;
                InterpretTrackedObjectData(lastTrackedData);
            }
            else
            {
                _sendRequest = true;
            }
            for(var i = 0; i < _receivedPackages; i++)
            {
                _receivedPackagesTimes.Add(Time.unscaledTime);
            }
            _receivedPackages = 0;
            var newReceivedPackagesTimes = new List<float>();
            foreach (var rpt in _receivedPackagesTimes)
            {
                if(Time.unscaledTime -1f < rpt)
                    newReceivedPackagesTimes.Add(rpt);
            }
            _receivedPackagesTimes = newReceivedPackagesTimes;
            //Debug.Log("frame rate : " + Time.deltaTime);
        }

        private bool GetLastTrackingData(out TrackedObjectData[] trackData)
        {
            trackData = new TrackedObjectData[0];
            var change = false;
            if (_dataBufferHead >= 0)
            {
                lock (syncLock)
                {
                    change = _lastHead != _dataBufferHead;
                    _lastHead = _dataBufferHead;
                    trackData = _dataBuffer[_dataBufferHead];
                }
            }
            return change;
        }

        private void CheckCreatedObjects()
        {
            var copy = new Dictionary<int, string>(_id2name);
            foreach(var id2Name in copy)
            {
                var setName = id2Name.Value;//data.name;
                                            //add to dict if does not exist
                if (!gameObjectDictionary.ContainsKey(setName))
                {
                    GameObject go;
                    var child = transform.Find(setName);
                    if (child != null)
                        go = child.gameObject;
                    else
                    {
                        go = new GameObject(setName);
                        go.transform.parent = transform;
                        createdObjectDictionary.Add(setName, go);
                    }
                    gameObjectDictionary.Add(setName, go);
                    go.tag = "untracked";
                }
            }

            //remove unused gameobjects
            var createdCopy = new Dictionary<string, GameObject>(createdObjectDictionary);
            foreach (var entry in createdCopy)
            {
                var isInDict = copy.ContainsValue(entry.Key);
                if (!isInDict)
                {
                    createdObjectDictionary.Remove(entry.Key);
                    gameObjectDictionary.Remove(entry.Key);
                    Destroy(entry.Value);
                }
            }

            //TODO throw created event
            OnNewTrackingData?.Invoke();
        }

        private static byte[] ToByteArray<T>(T[] source) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = handle.AddrOfPinnedObject();
                byte[] destination = new byte[source.Length * Marshal.SizeOf(typeof(T))];
                Marshal.Copy(pointer, destination, 0, destination.Length);
                return destination;
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
        
        private static T[] FromByteArray<T>(byte[] source) where T : struct
        {
            T[] destination = new T[source.Length / Marshal.SizeOf(typeof(T))];
            GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = handle.AddrOfPinnedObject();
                Marshal.Copy(source, 0, pointer, source.Length);
                return destination;
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

        private void InterpretTrackedObjectData(TrackedObjectData[] tod)
        {
            var tracked = new List<string>();
            foreach (var data in tod)
            {
                GameObject trackedObj;
                if (_id2name.ContainsKey(data.id))
                {
                    var setName = _id2name[data.id];//data.name;
                    //add to dict if does not exist
                    if (!gameObjectDictionary.TryGetValue(setName, out trackedObj))
                    {
                        Debug.Log("ViveTracking: game object not yet created");
                        continue;
                    }
                    //update transform
                    trackedObj.transform.localPosition = data.pos;
                    trackedObj.transform.localRotation = data.rot;

                    trackedObj.tag = "tracked";
                    tracked.Add(setName);
                } else
                {
                    _sendRequest = true;
                   // Debug.Log("ViveTracking: missing entry " + data.id);
                   // Debug.Log("ViveTracking: please request dictionary from server");
                }
            }

            //tag untracked objects
            foreach (var untracked in gameObjectDictionary)
            {
                if (tracked.Contains(untracked.Key))
                    continue;
                untracked.Value.tag = "untracked";
            }
        }

        public GameObject GetTrackable(string trackableName)
        {
            GameObject returnValue;
            gameObjectDictionary.TryGetValue(trackableName, out returnValue);
            return returnValue;
        }

        public GameObject[] GetAllTrackables()
        {
            return gameObjectDictionary.Values.ToArray();
        }
    }
}
