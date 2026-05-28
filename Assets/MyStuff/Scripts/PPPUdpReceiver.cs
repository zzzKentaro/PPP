using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class PPPUdpReceiver : MonoBehaviour
{
    [Header("UDP")]
    [SerializeField] private int listenPort = 5005;
    [SerializeField] private bool printDebugLog = false;

    [Header("References")]
    [SerializeField] private PPPObstacleManager obstacleManager;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool running;

    private readonly object frameLock = new object();
    private string latestJson;
    private bool hasNewFrame;

    [Serializable]
    private class ObstacleFrameDto
    {
        public int frame_w;
        public int frame_h;
        public PPPObstacleManager.ObstacleDto[] obstacles;
    }

    private void Start()
    {
        if (obstacleManager == null)
        {
            obstacleManager = FindObjectOfType<PPPObstacleManager>();
        }

        if (obstacleManager == null)
        {
            Debug.LogError("[PPP] PPPObstacleManager was not found.");
            enabled = false;
            return;
        }

        StartReceiver();
    }

    private void Update()
    {
        string json = null;

        lock (frameLock)
        {
            if (hasNewFrame)
            {
                json = latestJson;
                hasNewFrame = false;
            }
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            var frame = JsonUtility.FromJson<ObstacleFrameDto>(json);
            if (frame == null)
            {
                return;
            }

            obstacleManager.ApplyFrame(frame.obstacles);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PPP] Failed to parse JSON: {ex.Message}\n{json}");
        }
    }

    private void StartReceiver()
    {
        try
        {
            udpClient = new UdpClient(listenPort);
            running = true;
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            Debug.Log($"[PPP] UDP receiver started on port {listenPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PPP] Could not start UDP receiver: {ex.Message}");
            enabled = false;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(bytes);

                lock (frameLock)
                {
                    latestJson = json;
                    hasNewFrame = true;
                }

                if (printDebugLog)
                {
                    Debug.Log($"[PPP] Received {bytes.Length} bytes from {remoteEndPoint}");
                }
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PPP] ReceiveLoop error: {ex.Message}");
                Thread.Sleep(10);
            }
        }
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        running = false;

        try
        {
            udpClient?.Close();
            udpClient = null;
        }
        catch
        {
        }

        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
            }
        }
        catch
        {
        }
    }
}
