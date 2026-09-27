// Compile-check stubs for packages whose real assemblies aren't obtainable in
// this sandbox. Only the API surface the project touches. NOT shipped.
namespace UnityEngine.Rendering.Universal
{
    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public float renderScale { get; set; }
        public int msaaSampleCount { get; set; }
        public float shadowDistance { get; set; }
        protected override RenderPipeline CreatePipeline() { return null; }
    }
}
namespace Unity.Cinemachine
{
    using UnityEngine;
    public class CinemachineCore { public enum Stage { Body, Aim, Noise, Finalize } }
    public struct CameraState { public Quaternion RawOrientation; public Vector3 RawPosition; }
    public struct LensSettings { public float FieldOfView; public float NearClipPlane; public float FarClipPlane; }
    public abstract class CinemachineVirtualCameraBase : MonoBehaviour
    {
        public int Priority { get; set; }
        public Transform Follow { get; set; }
        public Transform LookAt { get; set; }
    }
    public class CinemachineVirtualCamera : CinemachineVirtualCameraBase
    {
        public LensSettings m_Lens;
        public T GetCinemachineComponent<T>() where T : class { return null; }
    }
    public class CinemachineBrain : MonoBehaviour
    {
        public ICinemachineCamera ActiveVirtualCamera { get; }
        public Camera OutputCamera { get; }
    }
    public interface ICinemachineCamera { }
    public abstract class CinemachineExtension : MonoBehaviour
    {
        protected virtual void Awake() { }
        protected abstract void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime);
    }
}
namespace Unity.Cinemachine { public class CinemachinePanTilt : UnityEngine.MonoBehaviour { public float PanAxisValue; public float TiltAxisValue; } }

// Unity 6.2 API present in the project's editor (6000.6) but not in the
// 6000.0.58 reference set: map GetEntityId onto GetInstanceID for compiling.
namespace UnityEngine
{
    public static class CodCompatEntityIdExtensions
    {
        public static int GetEntityId(this Object o) { return o.GetInstanceID(); }
    }
}
