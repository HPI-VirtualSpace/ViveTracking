using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Assets.Scripts
{
    [System.Serializable]
    public struct TrackedObjectData
    {
        public int id;
        public Vector3 pos;
        public Quaternion rot;
    }

    [RequireComponent (typeof(SteamVrAsyncStreamingMappingDataUsingTcp))]
    public class SteamVrStreamingTrackingDataUsingUdp : MonoBehaviour {

        [Header("Settings")]
        public Transform OrderCoordinateSystem;

        [Header("Steam Properties")]
        public int CreateNumber = 15;
        public int StartIndex = 1;

        [Header("Server IP")]
        public string IPServer = "192.168.116";
        public string MulticastAddress = "226.0.0.1";

        [Header("Client IPs")]
        public bool UseCustomIps;
        public List<string> ClientIps;

        [Header("Tracking Server Properties")]
        public int TrackingPort = 33000;
        public string SocketSize = "0x100000";
        public bool UseAnyIPAddress = false;
        public int SentFramesPerSecond = 60;
        public Vector3 TrackableRotationInitial;

        [Header("Mapping Server Properties")]
        public int MappingPort = 11000;
        public string CommandMappingTable = "REQUIRETABLE";
        public char MappingTableEntryValueSeparator = '-';
        public char MappingTableEntriesSeparator = ';';

        [Header("Logging Tracked Data")]
        public Logging Logging;
        
        private List<SteamVR_TrackedObject> _steamTrackedObjects;
        private List<SteamVR_TrackedObject> _steamTrackedObjectsInitial;
        private Dictionary<int, string> _lastDict;
        private List<IPEndPoint> _clientEndPoints;
        private float _initializeTime;
        private bool _initialized;
        private bool _tryIdentify;
        private int _tryIdentifyIndex;
        private List<Vector3> _savedPositions;
        private int SOCKET_BUFSIZE = 0x100000;
        private List<TrackedObjectData> _trackedObjectData;
        private List<int> _lastIds;
        private List<string> _lastNames;
        private List<bool> _lastActive;
        private Socket _socket;
        private IPEndPoint _endPoint;
        private byte[] _buffer;
        private List<SteamVR_Offset> _offsets;
        private SteamVrAsyncStreamingMappingDataUsingTcp _tcpServer;
        private float _nextTime;
        private List<SteamVR_TrackedObject> _objectsStart;

        private void Awake()
        {
            SOCKET_BUFSIZE = (int)new System.ComponentModel.Int32Converter().ConvertFromString(SocketSize);
            //init variables
            _tcpServer = GetComponent<SteamVrAsyncStreamingMappingDataUsingTcp>();
            _trackedObjectData = new List<TrackedObjectData>();
            _savedPositions = new List<Vector3>();
            _steamTrackedObjects = new List<SteamVR_TrackedObject>();
            _steamTrackedObjectsInitial = new List<SteamVR_TrackedObject>();
            _lastIds = new List<int>();
            _lastActive = new List<bool>();
            _lastNames = new List<string>();
            _offsets = new List<SteamVR_Offset>();
            _tcpServer.Port = 11000;
            _tcpServer._isListening = true;
            _tcpServer._ip = IPServer;
            _tcpServer.RequiredCommand = CommandMappingTable;
            _tcpServer.ServerMessageEntryValueSeparator = MappingTableEntryValueSeparator;
            _tcpServer.ServerMessageEntriesSeparator = MappingTableEntriesSeparator;

            //add to lists
            var _childrenWithTrackedObjects = GetComponentsInChildren<SteamVR_TrackedObject>().ToList();
            _objectsStart = new List<SteamVR_TrackedObject>(_childrenWithTrackedObjects);
            var wishList = _childrenWithTrackedObjects
                .Select(c => PlayerPrefs.GetInt(c.gameObject.name + "_id", -1)).ToList();
            var otherAvailableIds = new List<int>();
            var allAvailableIds = new List<int>();
            for (var i = StartIndex; i < CreateNumber; i++)
            {
                allAvailableIds.Add(i);
                if (!wishList.Contains(i))
                    otherAvailableIds.Add(i);
            }
            for (var i = 0; i < _childrenWithTrackedObjects.Count; i++)
            {
                var child = _childrenWithTrackedObjects[i];
                var childname = child.gameObject.name;
                if (wishList[i] >= 0)
                {
                    var ind = wishList[i];
                    if (allAvailableIds.Contains(ind))
                    {
                        Debug.Log("success: assign id " + ind + " to " + childname);
                    }
                    else
                    {
                        ind = otherAvailableIds.First();
                        Debug.Log("failed: assign id " + wishList[i] + " to " + childname + ", use " + ind + " instead");
                    }
                    child.index = (SteamVR_TrackedObject.EIndex)ind;
                    allAvailableIds.Remove(ind);
                    if(otherAvailableIds.Contains(ind))
                        otherAvailableIds.Remove(ind);
                    //child.gameObject.SetActive(PlayerPrefs.GetInt(childname + "_active") == 1);
                }
            }
            for (var i = StartIndex; i < CreateNumber; i++)
            {
                SteamVR_Offset off;
                var ind = i;
                var candidates = _childrenWithTrackedObjects.Where(s => (int)s.index == ind).ToList();
                var sto = candidates.FirstOrDefault();
                if (sto != null)
                {
                    off = sto.gameObject.GetComponent<SteamVR_Offset>();
                    _childrenWithTrackedObjects.Remove(sto);
                    Debug.Log("handle " + sto.gameObject.name);
                }
                else
                {
                    var go = new GameObject();
                    sto = go.AddComponent<SteamVR_TrackedObject>();
                    sto.index = (SteamVR_TrackedObject.EIndex)i;
                    go.transform.parent = transform;
                    go.name = "UnassignedName" + i;
                    Debug.Log("create " + go.name);
                    off = go.AddComponent<SteamVR_Offset>();

                    if (candidates.Count > 1)
                    {
                        Debug.Log("mutliple ids " + i);
                        var delete = _childrenWithTrackedObjects.Where(c => c != sto && (int) c.index == i);
                        foreach (var d in delete)
                        {
                            _childrenWithTrackedObjects.Remove(d);
                            Destroy(d.gameObject);
                        }
                    }
                }

                var newData = new TrackedObjectData
                {
                    id = i,
                    pos = Vector3.zero,
                    rot = Quaternion.identity
                };
                sto.origin = null;
                _trackedObjectData.Add(newData);
                _steamTrackedObjects.Add(sto);
                _steamTrackedObjectsInitial.Add(sto);
                _lastIds.Add((int)sto.index);
                _lastActive.Add(sto.gameObject.activeSelf);
                _lastNames.Add(sto.gameObject.name);
                _offsets.Add(off);
                _savedPositions.Add(sto.transform.position);
#if UNITY_EDITOR
                DrawIcon(sto.gameObject, 0);
                sto.gameObject.SetActive(true);
#endif
            }
            if (_childrenWithTrackedObjects.Count > 0)
            {
                Debug.LogError("Initialize error: more than one initial 'SteamVR_TrackedObject' with same device index");
            }  

            var ser = PlayerPrefs.GetString("origin");
            Vector3 pos;
            Quaternion rot;
            if (TryDeserializeOrigin(ser, out rot, out pos))
            {
                transform.rotation = rot;
                transform.position = pos;
            }

            Logging.SetTrackedObjects(_steamTrackedObjects);
        }

        private void ResetNames()
        {
            for (var i = 0; i < _offsets.Count; i++)
            {
                _offsets[i].SetNames(_lastNames.ToArray(), i);
            }
        }

        public void SetOriginPoint(Transform origin, bool isHmd)
        {
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            //var originRot = origin.rotation;
            //var originRotOnlyYaw = Quaternion.Euler(0, originRot.y, 0);
            //var origRotNoYaw = Quaternion.Euler(originRot.x, 0f, originRot.z);//, 0);
            if(!isHmd)
                transform.rotation = Quaternion.Euler(TrackableRotationInitial) * Quaternion.Inverse(origin.rotation);
            else
                transform.rotation = Quaternion.Inverse(origin.rotation);
            var rot = transform.rotation;
            //Debug.Log(transform.rotation.y);
            transform.rotation = Quaternion.Euler(0, transform.rotation.eulerAngles.y, 0);
            transform.position = -origin.position;
            var pos = transform.position;
            var serialize = SerializeOrigin(rot, pos);
            PlayerPrefs.SetString("origin", serialize);
        }

        public void Switch(string nameFrom, string nameTo)
        {
            Reinitialize();
            var from = _lastNames.IndexOf(nameFrom);
            var to = _lastNames.IndexOf(nameTo);
            _steamTrackedObjects[from].index = _steamTrackedObjects[to].index;
        }

        public void Reinitialize()
        {
            _initialized = false;
            _initializeTime = Time.time + 0.5f;
            foreach (var sto in _steamTrackedObjects)
                sto.gameObject.SetActive(true);
        }

        private class TrackedDistance
        {
            public SteamVR_TrackedObject TrackedObject;
            public float Distance;

            public TrackedDistance(SteamVR_TrackedObject trackedObject, float distance)
            {
                TrackedObject = trackedObject;
                Distance = distance;
            }
        }

        public void Order()
        {
            var trackedDistances = new List<TrackedDistance>(_steamTrackedObjects.Count);
            var inactiveIndex = 0;
            foreach (var sto in _steamTrackedObjects)
            {
                var relPos = 
                    OrderCoordinateSystem.InverseTransformPoint(
                        sto.gameObject.GetComponent<SteamVR_Offset>().Child.position
                    );
                var distToOrigin = sto.gameObject.activeSelf ? relPos.x : 1000f + inactiveIndex++;

                trackedDistances.Add(new TrackedDistance(sto, distToOrigin));
            }

            trackedDistances.Sort((tD1, tD2) => tD1.Distance.CompareTo(tD2.Distance));
            // needs to do it here because we reassign them for all devices now (switch a and b needs h)
            var newIndices = trackedDistances.Select(tD => tD.TrackedObject.index).ToList();

            for (int i = 0; i < trackedDistances.Count; i++)
            {
                Transform child = transform.GetChild(i);
                var sto = child.GetComponent<SteamVR_TrackedObject>();
                sto.index = newIndices[i];
            }
        }

        public void Identify(string name)
        {
            if (!_lastNames.Contains(name))
                return;
            for (var i = 0; i < _steamTrackedObjects.Count; i++)
            {
                var sto = _steamTrackedObjects[i];
                var pos = sto.transform.position;
                _savedPositions[i] = pos;
            }
            _tryIdentify = true;
            _tryIdentifyIndex = _lastNames.IndexOf(name);

            Debug.Log("try identify: " + name + ". Move object by > 10cm in order to proceed");
        }

        private bool TryDeserializeOrigin(string str, out Quaternion rotation, out Vector3 origin)
        {
            var split = str.Split(';');
            origin = Vector3.zero;
            rotation = Quaternion.identity;
            if (split.Length != 3)
                return false;
            var posStr = split[1].Split('$');
            var rotStr = split[2].Split('$');
            if (posStr.Length < 3 || rotStr.Length < 4)
                return false;
            origin = new Vector3(
                float.Parse(posStr[0]),
                float.Parse(posStr[1]),
                float.Parse(posStr[2]));
            rotation = new Quaternion(
                float.Parse(rotStr[0]),
                float.Parse(rotStr[1]),
                float.Parse(rotStr[2]),
                float.Parse(rotStr[3]));
            return true;
        }

        private string SerializeOrigin(Quaternion rotation, Vector3 position)
        {
            var str = "origin";
            str += ";";
            str += position.x + "$";
            str += position.y + "$";
            str += position.z;
            str += ";";
            str += rotation.x + "$";
            str += rotation.y + "$";
            str += rotation.z + "$";
            str += rotation.w;
            return str;
        }

        void Start()
        {
            //initialize command socket
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint endPoint;
            if (UseAnyIPAddress)
            {
                endPoint = new IPEndPoint(IPAddress.Any, 0);
            }
            else
            {
                IPAddress ipAddress;
                IPAddress.TryParse(IPServer, out ipAddress);
                endPoint = new IPEndPoint(ipAddress, TrackingPort);
            }
            _socket.Bind(endPoint);

            IPAddress mulIP = IPAddress.Parse(MulticastAddress);
            _socket.SetSocketOption(SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(mulIP, IPAddress.Any));

            //set socket to boradcast mode
            //_socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

            _socket.Blocking = false;
            _socket.ReceiveBufferSize = SOCKET_BUFSIZE;


            

            if (UseCustomIps)
            {
                _clientEndPoints = ClientIps.Select(c => new IPEndPoint(IPAddress.Parse(c), TrackingPort)).ToList();
            }
            else
            {
                _endPoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), TrackingPort);
            }

            Debug.Log("started " + _endPoint);

            Reinitialize();
        }

        private void DrawIcon(GameObject gameObject, int idx)
        {
            var largeIcons = GetTextures("sv_label_", string.Empty, 0, 8);
            var icon = largeIcons[idx];
            var egu = typeof(EditorGUIUtility);
            var flags = BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.NonPublic;
            var args = new object[] { gameObject, icon.image };
            var setIcon = egu.GetMethod("SetIconForObject", flags, null, new Type[] { typeof(UnityEngine.Object), typeof(Texture2D) }, null);
            setIcon.Invoke(null, args);
        }

        private GUIContent[] GetTextures(string baseName, string postFix, int startIndex, int count)
        {
            GUIContent[] array = new GUIContent[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = EditorGUIUtility.IconContent(baseName + (startIndex + i) + postFix);
            }
            return array;
        }
	
        void Update () {

            //try initialize
            if(!_initialized)
            {
                if(Time.time < _initializeTime)
                {
                    for (var i = 0; i < _steamTrackedObjects.Count; i++)
                    {
                        var sto = _steamTrackedObjects[i];
                        var pos = sto.transform.position;
                        _savedPositions[i] = pos;
                    }
                }
                else
                {
                    for (var i = 0; i < _steamTrackedObjects.Count; i++)
                    {
                        var sto = _steamTrackedObjects[i];
                        var pos = sto.transform.position;
                        var moved = Vector3.Distance(pos, _savedPositions[i]) > 0.00001f;
                        sto.gameObject.SetActive(moved);
                    }
                    _initialized = true;
                    ResetNames();
                }
                
            }
            else if (_tryIdentify)
            {
                for (var i = 0; i < _steamTrackedObjects.Count; i++)
                {
                    var sto = _steamTrackedObjects[i];
                    var pos = sto.transform.position;
                    var moved = Vector3.Distance(pos, _savedPositions[i]) > 0.1f;
                    if (moved)
                    {
                        _steamTrackedObjects[_tryIdentifyIndex].index = _steamTrackedObjects[i].index;
                        _tryIdentify = false;
                        break;
                    }
                }
            }
            else
            {
                //stream data
                _nextTime -= Time.deltaTime;
                if (_nextTime < 0f)
                {
                    _nextTime = 1f / SentFramesPerSecond + _nextTime;
                    StreamTrackedObjectData();
                }
            }
        }

        private void NotifyTcpServerMappingChanged()
        {
            var dict = new Dictionary<int, string>();
            for(var i = 0; i < CreateNumber - StartIndex; i++)
            {
                if(_steamTrackedObjects[i].gameObject.activeSelf)
                    dict.Add(_lastIds[i], _lastNames[i]);
                PlayerPrefs.SetInt(_lastNames[i] + "_id", _lastIds[i]);
                //PlayerPrefs.SetInt(_lastNames[i] + "_active", _steamTrackedObjects[i].gameObject.activeSelf ? 1: 0);
            }
            _tcpServer.UpdateAnswerString(dict);
            _lastDict = dict;
        }

        void StreamTrackedObjectData(){
            var ts = new List<TrackedObjectData>();
            
            var changed = false;
            for (var i = 0; i < CreateNumber-StartIndex; i++)
            {
                var data = _trackedObjectData[i];

                //check device ids (no doubles) and names (changes)
                var deviceId = (int)_steamTrackedObjects[i].index;
                var tmpName = _steamTrackedObjects[i].gameObject.name;
                var isActive = _steamTrackedObjects[i].gameObject.activeSelf;
                if (_lastNames[i] != tmpName)
                {
                    var lastName = _lastNames[i];
                    _lastNames[i] = tmpName;
                    Debug.Log("changed name of device " + _lastIds[i] + " from " + lastName + " to " + tmpName);
                    ResetNames();
                    changed = true;
                }
                if (_lastIds[i] != deviceId)
                {
                    var indexOfData2Change = -1;
                    TrackedObjectData data2Change = new TrackedObjectData();
                    for(var j = 0; j < CreateNumber - StartIndex; j++)
                    {
                        if (i == j || (int)_steamTrackedObjects[j].index != deviceId)
                            continue;
                        indexOfData2Change = j;
                        data2Change = _trackedObjectData[j];
                        break;                        
                    }
                    if(indexOfData2Change >= 0)
                    {

                        var otherActive = _steamTrackedObjects[indexOfData2Change].gameObject.activeSelf;

                        data2Change.id = _lastIds[i];
                        _steamTrackedObjects[indexOfData2Change].index = (SteamVR_TrackedObject.EIndex)_lastIds[i];
                        _lastIds[indexOfData2Change] = _lastIds[i];
                        _steamTrackedObjects[indexOfData2Change].gameObject.SetActive(isActive);

                        data.id = deviceId;
                        //_steamTrackedObjects[i].index = (SteamVR_TrackedObject.EIndex)deviceId;
                        _lastIds[i] = deviceId;
                        _steamTrackedObjects[i].gameObject.SetActive(otherActive);

                        Debug.Log("switched device ID of " + _steamTrackedObjects[i].gameObject.name
                                  + " (now " + deviceId + ") and " + _steamTrackedObjects[indexOfData2Change].gameObject.name + " (now " + data2Change.id + ")");
                        _trackedObjectData[i] = data;
                        _trackedObjectData[indexOfData2Change] = data2Change;

                        changed = true;
                    }
                }
                if(isActive != _lastActive[i])
                {
                    _lastActive[i] = isActive;

                    changed = true;
                }

                var streamAnyway = Logging.State == Logging.LogState.Playing || Logging.State == Logging.LogState.PlayingPause;
                if (!streamAnyway && (!_steamTrackedObjects[i].isValid || !isActive))
                    continue;

                if (_offsets[i].AnyDependencyViolated())
                    continue;
                data.pos = _offsets[i].Child.position;
                data.rot = (streamAnyway ? _offsets[i].Child.rotation * Quaternion.Euler(TrackableRotationInitial) : _offsets[i].Child.rotation);
                ts.Add(data);
            }

            if (changed)
            {
                NotifyTcpServerMappingChanged();
                Logging.SetTrackedObjects(_steamTrackedObjects);
            }

            TrackedObjectData[] tsa = ts.ToArray();
            byte[] msg = ToByteArray(tsa);
            if (_socket != null && ts.Count!=0)
            {
                if (UseCustomIps)
                {
                    foreach (var clientEndPoint in _clientEndPoints)
                        _socket.SendTo(msg, clientEndPoint);                    
                }
                else 
                    _socket.SendTo(msg,_endPoint);
            }

            //log stuff
            Logging.TryLog(tsa, _lastDict);
            Logging.TryAlter(ref _steamTrackedObjects);
        }

        public static byte[] ToByteArray<T>(T[] source) where T : struct
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

        public static T[] FromByteArray<T>(byte[] source) where T : struct
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


        void OnApplicationQuit(){
            _socket.Close();
        }

    }

    [CustomEditor(typeof(SteamVrStreamingTrackingDataUsingUdp))]
    public class SteamVrStreamingTrackingDataUsingUdpEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var myScript = (SteamVrStreamingTrackingDataUsingUdp)target;
            if (myScript == null)
                return;
            if (GUILayout.Button("Check tracking (only active trackers are active)"))
            {
                myScript.Reinitialize();
            }
            if (GUILayout.Button("Order (check z position and reset ids)"))
            {
                myScript.Order();
                myScript.Reinitialize();
            }
            DrawDefaultInspector();
        }
    }
}
