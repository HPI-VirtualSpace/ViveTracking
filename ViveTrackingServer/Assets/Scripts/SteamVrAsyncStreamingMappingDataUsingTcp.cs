using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Assets.Scripts
{ // State object for reading client data asynchronously
    public class StateObject
    {
        // Client  socket.
        public Socket WorkSocket = null;

        // Size of receive buffer.
        public const int BufferSize = 1024;

        // Receive buffer.
        public byte[] Buffer = new byte[BufferSize];

        // Received data string.
        public StringBuilder Sb = new StringBuilder();
    }

    public class SteamVrAsyncStreamingMappingDataUsingTcp : MonoBehaviour
    {
        internal int Port = 11000;
        internal bool _isListening = false;
        internal static bool IsListening = false;
        internal string RequiredCommand = "REQUIRETABLE";
        internal char ServerMessageEntriesSeparator = ';';
        internal char ServerMessageEntryValueSeparator = '-';

        private static string _requireCommand;
        internal string _ip = "192.168.1.116";
        internal static int port = 11000;
        internal static string ip = "192.168.1.116";
        public static Socket listener;


        private static char _serverMessageEntriesSeparator;
        private static char _serverMessageEntryValueSeparator;

        // Thread signal.
        public static bool allDone = true;

        public static List<Socket> clients;
    
        private static string _answerString;

        private void Awake()
        {
            _serverMessageEntriesSeparator = ServerMessageEntriesSeparator;
            _serverMessageEntryValueSeparator = ServerMessageEntryValueSeparator;
            _requireCommand = RequiredCommand;
        }

        private void Update()
        {
            IsListening = _isListening;
            if (IsListening)
            {
                if (port != Port || ip != _ip)
                {
                    port = Port;
                    ip = _ip;
                    StopListening();
                }
                if (listener == null) StartListening();
            }
        }

        public void UpdateAnswerString(Dictionary<int, string> dictionary)
        {
            _answerString = SerializeTcpMessage(dictionary);
            BroadcastToAllClients(_answerString);
        }

        private void OnApplicationQuit()
        {
            if (listener == null) StopListening();
            if (clients != null)
            {
                foreach (Socket client in clients)
                {
                    if (client.Connected)
                    {
                        client.Shutdown(SocketShutdown.Both);
                        client.Close();
                    }
                }
            }
        }

        public static void StartListening()
        {
            // Data buffer for incoming data.
            //byte[] bytes = new Byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            //IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddress = IPAddress.Parse(ip);
            //IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);

            // Create a TCP/IP socket.
            listener = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                Debug.Log("Start listening at " + port);
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
                StopListening();
            }
        
            try {
                if (allDone) {
                    // Start an asynchronous socket to listen for connections.
                    Debug.Log("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);
                    allDone = false;
                }
            }
            catch (Exception e) {
                Debug.LogError(e.ToString());
                StopListening();
            }
        }

        public static void StopListening()
        {
            IsListening = false;
            if (listener != null)
                listener.Close();
            allDone = true;
            listener = null;
        }

        private static string SerializeTcpMessage(Dictionary<int, string> dictionary)
        {
            var resultString = _requireCommand;
            var stringList = dictionary.Select(d => d.Key + "" + _serverMessageEntryValueSeparator + "" + d.Value).ToList();
            for (var s = 0; s < stringList.Count; s++)
            {
                resultString += stringList[s];
                if (s < stringList.Count - 1)
                    resultString += _serverMessageEntriesSeparator;
            }
            return resultString;
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            Debug.Log("Accepted a connection...");

            // Signal the main thread to continue.
            allDone = true;
            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            if (clients == null) clients = new List<Socket>();
            clients.Add(handler);

            // Create the state object.
            StateObject state = new StateObject();
            state.WorkSocket = handler;
            handler.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
            try {
                if (allDone) {
                    // Start an asynchronous socket to listen for connections.
                    Debug.Log("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);
                    allDone = false;
                }
            }
            catch (Exception e) {
                Debug.LogError(e.ToString());
                StopListening();
            }
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.WorkSocket;

            // Read data from the client socket.
            int bytesRead = handler.EndReceive(ar);
            if (bytesRead > 0)
            {
                string msg = Encoding.ASCII.GetString(
                    state.Buffer, 0, bytesRead);

                // Check for end-of-file tag. If it is not there, read
                // more data.
                var messageBack = msg == _requireCommand ? _answerString : "unknown command";
                Debug.Log("Read " + msg.Length + " bytes from socket. \nData : " + msg + "\nResponse : " + messageBack);
                Send(handler, messageBack);
                //foreach (Socket client in clients)
                //{
                //    if (client != handler && client.Connected)
                //        Send(client, msg);
                //}
            }

            handler.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
        }

        private static void BroadcastToAllClients(string msg)
        {
            if (clients == null)
                return;
            Debug.Log("Broadcast change " + msg);
            foreach (Socket client in clients)
            {
                if (client.Connected)
                    Send(client, msg);
            }
        }

        private static void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);
            Debug.Log("Sent '" + data + "' as message to client.");
            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                Debug.Log("Sent " + bytesSent + " bytes to client.");
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }
    }
}