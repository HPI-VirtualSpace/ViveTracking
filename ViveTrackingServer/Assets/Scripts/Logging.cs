using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Assets.Scripts;
using UnityEditor;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

public class Logging : MonoBehaviour
{
    internal string Path;
    internal LogState State;
    internal enum LogState
    {
        Void,
        Playing,
        PlayingPause,
        Recording
    }

    [Serializable]
    public class TrackingData
    {
        public byte[] TrackedObjData;
        public Dictionary<int, string> ObjIdToName;
        public float TimeStamp;

        public TrackingData(TrackedObjectData[] tod, Dictionary<int, string> idToName, float time)
        {
            TrackedObjData = SteamVrStreamingTrackingDataUsingUdp.ToByteArray(tod);
            ObjIdToName = idToName;
            TimeStamp = time;
        }
    }
    private List<TrackingData> _data;
    internal int _currentPlayIndex;
    internal float TimePlaying;
    internal float TimeRecordLength;
    private List<SteamVR_TrackedObject> _trackedObjects;

    void Start()
    {
        _data = new List<TrackingData>();
        Path = "";
    }

    public void SetTrackedObjects(List<SteamVR_TrackedObject> trackedObjects)
    {
        _trackedObjects = trackedObjects;
    }

    public void TryAlter(ref List<SteamVR_TrackedObject> trackedObjects)
    {
        if (State != LogState.Playing && State != LogState.PlayingPause)
            return;

        TimePlaying += State == LogState.Playing ? Time.unscaledDeltaTime : 0f;
        var thisTime = TimePlaying + _data[0].TimeStamp;
        var end = true;
        for (var i = _currentPlayIndex; i < _data.Count; i++)
        {
            if (thisTime < _data[i].TimeStamp)
            {
                _currentPlayIndex = i;
                end = false;
                break;
            }
        }
        if (end && State == LogState.Playing)
        {
            _currentPlayIndex = 0;
            TimePlaying = 0f;
        }
        
        var lastPlayIndex = _currentPlayIndex == 0 ? 0 : _currentPlayIndex - 1;

        var diffTime = _data[_currentPlayIndex].TimeStamp - _data[lastPlayIndex].TimeStamp;
        var progress = diffTime > 0f ? (thisTime - _data[lastPlayIndex].TimeStamp) / diffTime : 1f;

        var currentData = SteamVrStreamingTrackingDataUsingUdp.FromByteArray<TrackedObjectData>(_data[_currentPlayIndex].TrackedObjData);
        var lastData = SteamVrStreamingTrackingDataUsingUdp.FromByteArray<TrackedObjectData>(_data[lastPlayIndex].TrackedObjData);
        
        for (var t = 0; t < currentData.Length; t++)
        {
            var tod = currentData[t];
            if (!lastData.Any(x => x.id == tod.id))
                continue;
            string name;
            if (!_data[_currentPlayIndex].ObjIdToName.TryGetValue(tod.id, out name))
                continue;
            var obj = trackedObjects.FirstOrDefault(to => to.name == name);
            if (obj == null)
                continue;

            var todLast = lastData.First(x => x.id == tod.id); ;
            obj.transform.position = Vector3.Lerp(todLast.pos, tod.pos, progress);
            obj.transform.rotation = Quaternion.Lerp(todLast.rot, tod.rot, progress);
        }
    }

    public void TryLog(TrackedObjectData[] tod, Dictionary<int, string> lastDict)
    {
        if (State != LogState.Recording)
            return;
        var timeStamp = Time.unscaledTime;
        TrackingData currentData = new TrackingData(tod, lastDict, Time.unscaledTime);
        _data.Add(currentData);
    }

    internal void StopPlaying()
    {
        foreach (var to in _trackedObjects)
            to.enabled = true;
        Debug.Log("STOP PLAYING - " + Path);
        State = LogState.Void;
    }

    internal void StopRecording()
    {
        BinaryFormatter bf = new BinaryFormatter();
        Debug.Log("STOP RECORDING - Saved to: " + Path);
        FileStream file = File.Create(Path);
        bf.Serialize(file, _data);
        file.Close();
        State = LogState.Void;
    }

    internal void LoadAndPlay()
    {
        if (!File.Exists(Path))
            return;
        foreach (var to in _trackedObjects)
            to.enabled = false;
        BinaryFormatter bf = new BinaryFormatter();
        FileStream file = File.Open(Path, FileMode.Open);
        _data = (List<TrackingData>)bf.Deserialize(file);
        file.Close();
        Debug.Log("PLAY - " + Path);

        _currentPlayIndex = 0;
        TimeRecordLength = _data.Last().TimeStamp - _data.First().TimeStamp;
        TimePlaying = 0f;// Time.unscaledTime;
        State = LogState.Playing;
    }

    internal void StartRecording()
    {
        //TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        var now = DateTime.UtcNow.ToLocalTime();
        var time = now.Hour + "-" + now.Minute + "-" + now.Second + "_" + now.Day + "-" + now.Month + "-" + now.Year;
        string fileName = Application.persistentDataPath + "/" + "logData_" + time + ".gd";
        Path = fileName;
        State = LogState.Recording;
        Debug.Log("START RECORDING - " + Path);
    }

    internal void OpenLogFolder()
    {
        string path = EditorUtility.OpenFilePanel("Load Log File", Application.persistentDataPath, "gd");
        string[] files = Directory.GetFiles(path);
        if (files.Length == 0)
            return;
        Path = files[0];
    }
}

[CustomEditor(typeof(Logging))]
public class LoggingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorUtility.SetDirty(target);
        if (!Application.isPlaying)
            return;
        Logging myScript = (Logging)target;

        var split = myScript.Path.Split('/');
        var path = split.Length == 0 ? "" : split.Last();
        GUILayout.Label("Current file : " + path);

        switch (myScript.State)
        {
            case Logging.LogState.Playing:
            case Logging.LogState.PlayingPause:
                var selectedTime = EditorGUILayout.Slider("Time", myScript.TimePlaying, 0f, myScript.TimeRecordLength);
                //Debug.Log(myScript.TimePlaying);// + " " + selectedTime);
                if(Mathf.Abs(selectedTime-myScript.TimePlaying) > 0.001f)
                {
                    myScript.TimePlaying = selectedTime;
                    myScript._currentPlayIndex = 0;
                }
                if (GUILayout.Button(myScript.State == Logging.LogState.PlayingPause ? "Continue" : "Pause"))
                    myScript.State = myScript.State == Logging.LogState.PlayingPause ? Logging.LogState.Playing : Logging.LogState.PlayingPause;
                if (GUILayout.Button("Stop"))
                    myScript.StopPlaying();
                break;
            case Logging.LogState.Recording:
                if (GUILayout.Button("Stop + Save Tracking Data"))
                    myScript.StopRecording();
                break;
            case Logging.LogState.Void:
                if (GUILayout.Button("Open Log Folder"))
                    myScript.OpenLogFolder();
                if(path.Length > 0)
                    if (GUILayout.Button("Play"))
                        myScript.LoadAndPlay();
                if (GUILayout.Button("Start Recording"))
                    myScript.StartRecording();
                break;
            
        }
    }
}