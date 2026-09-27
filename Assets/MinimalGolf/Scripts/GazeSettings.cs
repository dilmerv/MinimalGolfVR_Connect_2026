using Oculus.Interaction.Input;
using UnityEngine;

namespace MinimalGolf
{
    /// <summary>
    /// Inspector-driven eye gaze simulation switch.
    /// When Simulate Gaze is on, OVREyeGazeDataSource is active and the EyeGaze
    /// pose is emulated from the camera pose; when off, the data source is
    /// deactivated. References auto-find when left empty.
    /// </summary>
    public sealed class GazeSettings : MonoBehaviour
    {
        [Header("Simulate Gaze")]
        [Tooltip("When true, OVREyeGazeDataSource is active and gaze is emulated with the camera pose.")]
        public bool simulateGaze = true;

        [Header("References (auto-found if empty)")]
        public GameObject eyeGazeDataSource;
        public EyeGaze eyeGaze;

        private void Start()
        {
            CacheReferences();
            ApplyGazeSimulation();
        }

        private void OnValidate()
        {
            CacheReferences();
            ApplyGazeSimulation();
        }

        private void Update()
        {
            CacheReferences();
            ApplyGazeSimulation();
        }

        private void CacheReferences()
        {
            if (eyeGazeDataSource == null)
                eyeGazeDataSource = GameObject.Find("OVREyeGazeDataSource");
            if (eyeGaze == null)
                eyeGaze = FindAnyObjectByType<EyeGaze>();
        }

        private void ApplyGazeSimulation()
        {
            if (eyeGazeDataSource != null)
                eyeGazeDataSource.SetActive(simulateGaze);
            if (simulateGaze && eyeGaze != null)
                eyeGaze.EmulateGazeWithCameraPose = true;
        }
    }
}
