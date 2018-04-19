using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityThreading;
using UnityEngine.Networking;

namespace Assets.ViveClient
{

    // State object for reading client data asynchronously
    public class StateObject
    {
        // Client  socket.
        public Socket WorkSocket;

        // Size of receive buffer.
        public const int BufferSize = 1024;

        // Receive buffer.
        public byte[] Buffer = new byte[BufferSize];

        // Received data string.
        public StringBuilder Sb = new StringBuilder();
    }

    public class AsyncTcpSocket : MonoBehaviour
    {
        [HideInInspector]
        public int Port = 11000;
        [HideInInspector]
        public string ServerIp = "192.168.1.161";

        private static int _staticPort;
        private static string _staticServerIp;
        public static event Action<string> OnReceiveMessage;

        // The port number for the remote device.

        // ManualResetEvent instances signal completion.
        public static readonly ManualResetEvent ConnectDone =
            new ManualResetEvent(false);

        public static readonly ManualResetEvent SendDone =
            new ManualResetEvent(false);

        public static ManualResetEvent ReceiveDone =
            new ManualResetEvent(false);

        // The response from the remote device.
        //private static String response = String.Empty;

        private static Socket client;

        private static ActionThread connectionThread;

        private static bool isConnecting = false;
        
        public static string ServerMessage;

        private void Start()
        {
            if (client != null) return;

            _staticPort = Port;
            _staticServerIp = ServerIp;
            connectionThread = UnityThreadHelper.CreateThread(() => { StartClient(); });
        }

        private void Update() {
            if (!isConnecting && !client.Connected) {
                connectionThread.Abort();
                client.Close();
                connectionThread = UnityThreadHelper.CreateThread(() => { StartClient(); });
            }
        }

        public void Quit()
        {
            StopClient();
            connectionThread.AbortWaitForSeconds(3.0f);
        }

        private void OnApplicationQuit()
        {
            Quit();
        }

        private static void StartClient()
        {
            // Connect to a remote device.
            try
            {
                // Establish the remote endpoint for the socket.
                IPAddress ipAddress = IPAddress.Parse(_staticServerIp);
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, _staticPort);

                // Create a TCP/IP socket.
                client = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
                isConnecting = true;
                // Connect to the remote endpoint.
                client.BeginConnect(remoteEP,
                    new AsyncCallback(ConnectCallback), client);
                ConnectDone.WaitOne();
                isConnecting = false;
                Debug.Log("ViveTracking: Tcp Server Init");
                // Start Receive the response from the remote device.
                Receive(client);
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        private static void StopClient()
        {
            if (client.Connected)
            {
                client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
        }

        private static void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);
                Debug.Log("ViveTracking: Socket connected to " +
                          client.RemoteEndPoint.ToString());

                // Signal that the connection has been made.
                ConnectDone.Set();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        private static void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.WorkSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            //bool socketClosed = false;
            try
            {
                // Retrieve the state object and the client socket
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket workSocket = state.WorkSocket;

                // Read data from the remote device.
                int bytesRead = workSocket.EndReceive(ar);

                if (bytesRead > 0)
                {
                    string msg = Encoding.ASCII.GetString(state.Buffer, 0, bytesRead);
                    if(OnReceiveMessage!=null) UnityThreadHelper.Dispatcher.Dispatch(()=> {
                        OnReceiveMessage(msg);
                        //Debug.Log("receive: " + msg);
                    });

                    ServerMessage = msg;
                }
            
                //continue to receive data
                workSocket.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                if (e is ObjectDisposedException) {
                    //socketClosed = true;
                }
                else {
                    Debug.LogError(e.ToString());
                }
            }
        }

        public static void Send(String data)
        {
            if (client.Connected)
            {
                Send(client, data);
            }
        }

        public static bool IsConnected()
        {
            return client != null && client.Connected;
        }

        public static void Send(Socket client, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            client.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), client);
            SendDone.WaitOne();
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                client.EndSend(ar);

                // Signal that all bytes have been sent.
                SendDone.Set();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }
    }
}






