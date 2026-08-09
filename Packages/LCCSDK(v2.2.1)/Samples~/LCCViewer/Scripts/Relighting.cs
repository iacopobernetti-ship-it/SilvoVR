using System.Collections.Generic;
using LCCCore;
using Unity.Mathematics;
using UnityEngine;

public class Relighting : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    private LCCCore.Renderer m_renderer;

    [Header("Point Light Settings")]
    public Vector3 pointLightPosition = new Vector3(0, 2, 0);
    public Color pointLightColor = Color.red;
    public float pointLightIntensity = 2.0f;
    public float pointLightRange = 3.0f;

    [Header("Spot Light Settings")]
    public Vector3 spotLightPosition = new Vector3(0, 2, -2);
    public Vector3 spotLightDirection = new Vector3(0, -1, 0);
    public Color spotLightColor = new Color(0.1f, 0.9f, 0.7f);
    public float spotLightIntensity = 3.0f;
    public float spotLightRange = 20.0f;
    public float spotLightAngle = 45.0f;

    [Header("Animation")]
    public bool animateLights = true;
    public float rotateSpeed = 30.0f;

    private List<PointLightData> m_lights = new List<PointLightData>();
    private float m_angle;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);
        m_manager.SetMainCamera(Camera.main);

        m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath, onProgress, onComplete);
    }

    private void onProgress(float _progress)
    {
        Debug.Log($"[Relighting] load progress: {_progress:P1}");
    }

    private void onComplete()
    {
        Debug.Log("[Relighting] load complete, setting up lights");
        //m_manager.SetLightIntensity(0.5f);
        ApplyLights();
    }

    void Update()
    {
        if (!animateLights) return;

        m_angle += rotateSpeed * Time.deltaTime;
        ApplyLights();
    }

    private void ApplyLights()
    {
        m_lights.Clear();

        float rad = m_angle * Mathf.Deg2Rad;
        float px = pointLightPosition.x + Mathf.Cos(rad) * 3f;
        float pz = pointLightPosition.z + Mathf.Sin(rad) * 3f;

        // lightType 0 = point light
        m_lights.Add(new PointLightData
        {
            position = new float3(px, pointLightPosition.y, pz),
            lightType = 0,
            color = new float3(pointLightColor.r, pointLightColor.g, pointLightColor.b),
            intensity = pointLightIntensity,
            direction = float3.zero,
            range = pointLightRange,
            spotAngle = 0f,
            pad = float3.zero
        });

        // lightType 1 = spot light
        Vector3 dir = spotLightDirection.normalized;
        m_lights.Add(new PointLightData
        {
            position = new float3(spotLightPosition.x, spotLightPosition.y, spotLightPosition.z),
            lightType = 1,
            color = new float3(spotLightColor.r, spotLightColor.g, spotLightColor.b),
            intensity = spotLightIntensity,
            direction = new float3(dir.x, dir.y, dir.z),
            range = spotLightRange,
            spotAngle = spotLightAngle,
            pad = float3.zero
        });

        int result = m_manager.SetLights(m_lights);
        if (result != 0)
            Debug.LogWarning($"[Relighting] SetLights failed: {result}");
    }

    void OnDisable()
    {
        // set null to shut down lighting
        m_manager.SetLights(null);
    }
}
