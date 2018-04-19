using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CameraRigForViveTracking : MonoBehaviour
{

    //public Vector3 RotationalOffset = new Vector3(-90f, 0f, 180f);
    
	void Start () {
	    var ser = PlayerPrefs.GetString("origin");
	    Vector3 pos;
	    Quaternion rot;
	    if (TryDeserializeOrigin(ser, out rot, out pos))
	    {
	        transform.rotation = rot;
	        transform.position = pos;
	    }

	    Camera.main.transform.parent = transform;
	    Camera.main.transform.localPosition = Vector3.zero;
    }
    
    void Update () {
		
	}

    public void SetOriginPoint()
    {
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.rotation = Quaternion.Inverse(Camera.main.transform.rotation);
        var rot = transform.rotation;
        transform.position = -Camera.main.transform.position;
        transform.rotation = Quaternion.Euler(0, transform.rotation.eulerAngles.y, 0);
        var pos = transform.position;
        var serialize = SerializeOrigin(rot, pos);
        PlayerPrefs.SetString("origin", serialize);
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



}


[CustomEditor(typeof(CameraRigForViveTracking))]
public class ObjectBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var myScript = (CameraRigForViveTracking)target;
        if (myScript == null)
            return;
        if (GUILayout.Button("Set as Origin"))
        {
            myScript.SetOriginPoint();
        }
        DrawDefaultInspector();
    }
}
