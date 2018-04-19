using System.Collections;
using Assets.ViveClient;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace Assets
{
    public class CameraRig : MonoBehaviour {
        [HideInInspector]
        public bool Tracked;
        [HideInInspector] private GameObject _target;

        public string HmdPrefix = "HMD";
        public SteamVrReceiver Receiver;
        public Text DebugText;

        private bool _isCalibrated;
        private const float TimeForCalibration = 3.0f;
        private float _calibratingTime;
        private const float AngleOffsetThreshold = 3.0f;
        private Quaternion _angleAxisAddRot;
        private Transform _camTrans;
        private int _currentIndex;

        public enum VideoSettings
        {
            LowMobile,
            HighMobile,
            EditorPreview
        }

        public void Awake()
        {
            Tracked = true;
            _currentIndex = -1;
            _camTrans = Camera.main.transform;
            Camera.main.transform.parent = transform;
            UnityEngine.VR.InputTracking.disablePositionalTracking = true;
            Camera.main.transform.localPosition = Vector3.zero;
        }

        private void Update()
        {
            if(Input.GetMouseButtonDown(0))
                StartFollowingNext();
        }

        public void StartFollowingNext()
        {
            _angleAxisAddRot = Quaternion.identity;
            var targets = Receiver.GetAllTrackables().Where(r => r.name.StartsWith(HmdPrefix)).ToList();
            if (++_currentIndex >= targets.Count)
                _currentIndex = 0;
            if (targets.Count == 0)
            {
                if (DebugText != null)
                    DebugText.text = "No HMD tracked";
                return;
            }
            _target = targets[_currentIndex];
            if(DebugText != null)
                DebugText.text = _target.name;
            StopAllCoroutines();
            StartCoroutine(Following(_target));
        }

        public void SetGraphicsQuality(VideoSettings vs)
        {
            if (Application.isEditor)
            {
                vs = VideoSettings.EditorPreview;
            }
            switch (vs)
            {
                case VideoSettings.EditorPreview:
                    UnityEngine.VR.VRSettings.renderScale = 1f;
                    Application.targetFrameRate = 60;
                    break;
                case VideoSettings.HighMobile:
                    UnityEngine.VR.VRSettings.renderScale = 0.7f;
                    Application.targetFrameRate = 60;
                    break;
                case VideoSettings.LowMobile:
                    UnityEngine.VR.VRSettings.renderScale = 0.4f;
                    Application.targetFrameRate = 40;
                    break;
                default:
                    UnityEngine.VR.VRSettings.renderScale = 1f;
                    break;
            }
        }

        private IEnumerator Following(GameObject target){
            while(true){
                yield return null;
                transform.position = target.transform.position;
                _camTrans.localPosition = Vector3.zero;
                Tracked = target.tag != "untracked";
                if (target.tag == "untracked") { }
                else
                {
                    if (Application.isEditor)
                    {
                        transform.rotation = target.transform.rotation * _angleAxisAddRot;//since cam doesn't move

                        var cam = transform.GetChild(0);
                        cam.localRotation = Quaternion.identity;
                    }
                    else
                    {
                        //constantly correct orientation from optitrack -- problematic with drop outs
                        Quaternion twist;
                        Quaternion swing;
                        var targetRot = target.transform.rotation * _angleAxisAddRot;// angleAxisAddRot;// Quaternion.Euler(eulerOffset);
                        SwingTwistDecomposition(targetRot, Vector3.up, out twist, out swing);

                        var child = transform.GetChild(0);

                        Quaternion cameraTwist;
                        Quaternion cameraSwing;
                        SwingTwistDecomposition(child.transform.localRotation, Vector3.up, out cameraTwist, out cameraSwing);

                        var offset = Quaternion.Angle(transform.rotation, twist * Quaternion.Inverse(cameraTwist));

                        if (offset > AngleOffsetThreshold)
                        {
                            _isCalibrated = false;
                            _calibratingTime = 0.0f;
                        }
                        else if (_calibratingTime < TimeForCalibration)
                        {
                            _calibratingTime += Time.deltaTime;
                        }
                        else//if (!isCalibrated && offset < angleOffsetJumpBack)
                        {
                            transform.rotation = twist * Quaternion.Inverse(cameraTwist);
                            _isCalibrated = true;
                        }

                        if (!_isCalibrated)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, twist * Quaternion.Inverse(cameraTwist), 0.01f);
                        }
                    }
                }
            }
        }

        private void ResetOrientation(){
            Quaternion twist;
            Quaternion swing;
            SwingTwistDecomposition(_target.transform.rotation, Vector3.up, out twist, out swing);

            var child = transform.GetChild(0);

            Quaternion cameraTwist;
            Quaternion cameraSwing;
            SwingTwistDecomposition(child.transform.localRotation, Vector3.up, out cameraTwist, out cameraSwing);

            transform.rotation = twist * Quaternion.Inverse(cameraTwist);
        }

        //private static void SetTintColor(Material mat, Color c) {
        //	mat.SetColor("_TintColor", c);
        //}

        private void SwingTwistDecomposition(Quaternion q, Vector3 v, out Quaternion twist, out Quaternion swing){
            var rotationAxis = new Vector3(q.x, q.y, q.z);
            var projection = Vector3.Project(rotationAxis, v);
            var magnitude = Mathf.Sqrt(Mathf.Pow(projection.x, 2) + Mathf.Pow(projection.y, 2) + Mathf.Pow(projection.z, 2) +Mathf.Pow(q.w, 2));
            twist = new Quaternion(projection.x/magnitude, projection.y/magnitude, projection.z/magnitude, q.w/magnitude);
            var twistConjugated = new Quaternion(-projection.x/magnitude, -projection.y/magnitude, -projection.z/magnitude, q.w/magnitude);
            swing = q * twistConjugated;
        }
    }
}
