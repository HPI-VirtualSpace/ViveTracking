using System.Linq;
using Assets.Scripts;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(SteamVR_TrackedObject))]
public class SteamVR_Offset : MonoBehaviour
{
    [HideInInspector] public Transform Child;

    [SerializeField]
    public Vector3 PositionOffset;
    [SerializeField]
    public Vector3 RotationOffset;
    public SteamVR_Offset[] Dependencies;
    public float[] DependencyValues;
    public DependencyProperty[] DependencyProperties;
    
    public enum DependencyProperty
    {
        DistanceBiggerThanOrEqual,
        DistanceSmallerThanOrEqual
    }

    internal string[] Names;
    internal int SelectedNameIndex;
    internal int NameIndex;

    private SteamVrStreamingTrackingDataUsingUdp _tracking;
    private SteamVR_TrackedObject _trackedObject;

    private void Awake()
    {
        SetNames(new string[] { gameObject.name }, 1);
        SetChild();
    }

    private void OnValidate()
    {
        SetChild();
    }

    public bool AnyDependencyViolated()
    {
        if (Dependencies == null)
            return false;
        for(var i = 0; i<Dependencies.Length;i++)
        {
            if (DependencyProperties.Length <= i || DependencyValues.Length <= i)
                break;
            var val = DependencyValues[i];
            var dep = Dependencies[i];
            var prop = DependencyProperties[i];
            var dist = Vector3.Distance(transform.position, dep.transform.position);
            switch (prop) {
                case DependencyProperty.DistanceBiggerThanOrEqual:
                    if (dist < val)
                        return true;
                    break;
                case DependencyProperty.DistanceSmallerThanOrEqual:
                    if (dist > val)
                        return true;
                    break;
            }
        }
        return false;
    }

    public void SetNames(string[] names, int yourIndex)
    {
        Names = names;
        NameIndex = yourIndex;
        SelectedNameIndex = yourIndex;
    }

    private void SetChild()
    {
        if (Child == null || Child.gameObject == null || _tracking == null || _trackedObject == null)
        {
            if (Child == null)
            {
                var nameObject = gameObject.name + "_transmittedPosition";
                Child = transform.Find(name);
                if (Child == null)
                {
                    var go = new GameObject
                    {
                        name = nameObject
                    };
                    go.transform.parent = transform;
                    Child = go.transform;
                }
            }
            
            _tracking = transform.parent.GetComponent<SteamVrStreamingTrackingDataUsingUdp>();

            _trackedObject = GetComponent<SteamVR_TrackedObject>();
            _trackedObject.origin = transform.parent;
        }

        Child.localPosition = new Vector3(-PositionOffset.x, PositionOffset.z, PositionOffset.y);
        Child.localRotation = Quaternion.Euler(_tracking.TrackableRotationInitial)* Quaternion.Euler(RotationOffset);
    }

    internal void SetAsOrigin(bool withOffset)
    {
        _tracking.SetOriginPoint(transform, withOffset);
    }

    internal void Switch()
    {
        _tracking.Switch(Names[NameIndex], Names[SelectedNameIndex]);
        SelectedNameIndex = NameIndex;
    }

    internal void CheckAgain()
    {
        _tracking.Reinitialize();
    }

    internal void Identify()
    {
        _tracking.Identify(gameObject.name);
    }

    internal void Order()
    {

        _tracking.Order();
    }
}

[CustomEditor(typeof(SteamVR_Offset))]
public class ObjectBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var myScript = (SteamVR_Offset)target;
        if (myScript == null)
            return;
        if (GUILayout.Button("Set as Origin (for trackables)"))
        {
            myScript.SetAsOrigin(false);
        }
        if (GUILayout.Button("Set as Origin (for HMD)"))
        {
            myScript.SetAsOrigin(true);
        }
        //if (GUILayout.Button("Check tracking (only active trackers are active)"))
        //{
        //    myScript.CheckAgain();
        //}
        if (GUILayout.Button("Identify! (move tracked object by at least 10cm)"))
        {
            myScript.Identify();
        }
        //if (GUILayout.Button("Order (check z position and reset ids)"))
        //{
        //    myScript.Order();
        //}
        if (myScript.Names != null && myScript.Names.Any())
        {
            var options = myScript.Names;
            myScript.SelectedNameIndex = EditorGUILayout.Popup("Switch", myScript.SelectedNameIndex, options);
            if (myScript.SelectedNameIndex != myScript.NameIndex)
            {
                myScript.Switch();
            }
        }
        GUILayout.Label(myScript.AnyDependencyViolated() ? "DEPENDENCY VIOLATED" : "dependencies fulfilled");
        DrawDefaultInspector();
    }
}
