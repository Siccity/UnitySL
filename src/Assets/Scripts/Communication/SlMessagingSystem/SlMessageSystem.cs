﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class SlMessageSystem : IDisposable
{
    public static SlMessageSystem Instance = new SlMessageSystem();

    /// <summary>
    /// Local UDP port number
    /// </summary>
    public int Port { get; protected set; } = 13028;

    public Int32 SystemVersionMajor { get; protected set; }
    public Int32 SystemVersionMinor { get; protected set; }
    public Int32 SystemVersionPatch { get; protected set; }
    public Int32 SystemVersionServer { get; protected set; }

    /// <summary>
    /// Set this flag to TRUE when you want *very* verbose logs.
    /// </summary>
    public bool VerboseLog { get; set; } = false;

    public bool Error { get; protected set; } = false;
    public Int32 ErrorCode { get; protected set; } = 0;

    /// <summary>
    /// Does the outgoing message require a pos ack?
    /// </summary>
    public bool IsSendReliable { get; set; } = false;

    //mUnackedListDepth = 0;
    //mUnackedListSize = 0;
    //mDSMaxListDepth = 0;

    //mNumberHighFreqMessages = 0;
    //mNumberMediumFreqMessages = 0;
    //mNumberLowFreqMessages = 0;
    //mPacketsIn = mPacketsOut = 0;
    //mBytesIn = mBytesOut = 0;
    //mCompressedPacketsIn = mCompressedPacketsOut = 0;
    //mReliablePacketsIn = mReliablePacketsOut = 0;

    //mCompressedBytesIn = 0;
    //mCompressedBytesOut = 0;
    //mUncompressedBytesIn = 0;
    //mUncompressedBytesOut = 0;
    //mTotalBytesIn = 0;
    //mTotalBytesOut = 0;

    //mDroppedPackets = 0;            // total dropped packets in
    //mResentPackets = 0;             // total resent packets out
    //mFailedResendPackets = 0;       // total resend failure packets out
    //mOffCircuitPackets = 0;         // total # of off-circuit packets rejected
    //mInvalidOnCircuitPackets = 0;   // total # of on-circuit packets rejected

    //mOurCircuitCode = 0;

    //mIncomingCompressedSize = 0;
    //mCurrentRecvPacketID = 0;

    //mMessageFileVersionNumber = 0.f;

    //mTimingCallback = NULL;
    //mTimingCallbackData = NULL;

    //mMessageBuilder = NULL;

    protected Dictionary<IPEndPoint, Circuit> CircuitByEndPoint = new Dictionary<IPEndPoint, Circuit>();
    protected UdpClient UdpClient;

    protected class OutgoingMessage
    {
        public Circuit Circuit { get; set; }
        public byte[] MessageBytes { get; set; }
    }

    protected Queue<OutgoingMessage> OutGoingMessages = new Queue<OutgoingMessage>(); // ConcurrentQueue?

    protected class UdpState
    {
        public UdpClient Client;
        public IPEndPoint EndPoint;
    }

    public void Start()
    {
        if (_threadLoopTask != null && _threadLoopTask.Status == TaskStatus.Running)
        {
            Logger.LogDebug("SlMessageSystem.Start: Already started.");
            return;
        }
        Logger.LogDebug("SlMessageSystem.Start");

        _cts = new CancellationTokenSource();
        _threadLoopTask = Task.Run(() => ThreadLoop(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        Logger.LogDebug("SlMessageSystem.Stop");
        _cts.Cancel();

        foreach (Circuit circuit in CircuitByEndPoint.Values)
        {
            circuit.Stop();
        }

        _cts.Dispose();
        UdpClient.Close();
        UdpClient?.Dispose();
    }

    private CancellationTokenSource _cts;
    private Task _threadLoopTask;

    protected async Task ThreadLoop(CancellationToken ct)
    {
        UdpState state = new UdpState();
        state.EndPoint = new IPEndPoint(IPAddress.Any, Port);
        state.Client = new UdpClient(state.EndPoint);
        UdpClient = state.Client;
        Logger.LogInfo("SlMessageSystem.ThreadLoop: Running");

        UdpClient.BeginReceive(ReceiveData, state);
        while (ct.IsCancellationRequested == false)
        {
            while (OutGoingMessages.Count > 0 && ct.IsCancellationRequested == false)
            {
                try
                {
                    OutgoingMessage om = OutGoingMessages.Dequeue();
                    await Send(om.MessageBytes, om.Circuit);
                }
                catch (Exception e)
                {
                    Logger.LogError($"SlMessageSystem.ThreadLoop: {e}");
                }
            }

            //try
            //{
            //    //Byte[] data = UdpClient.Receive(ref endPoint);
            //    //Logger.LogDebug("Message: " + BitConverter.ToString(data));
            //}
            //catch (SocketException ex)
            //{
            //    if (ex.ErrorCode != 10060)
            //    {
            //        //Logger.LogDebug("a more serious error " + ex.ErrorCode);
            //    }
            //    else
            //    {
            //        //Logger.LogDebug("expected timeout error");
            //    }
            //}

            await Task.Delay(10, ct); // tune for your situation, can usually be omitted
        }
        // Cancelling appears to kill the task immediately without giving it a chance to get here
        Logger.LogInfo($"SlMessageSystem.ThreadLoop: Stopping...");
        UdpClient.Close();
        UdpClient?.Dispose();
    }

    public Circuit EnableCircuit(string address, int port, float heartBeatInterval = 5f, float circuitTimeout = 100f)
    {
        IPAddress a = IPAddress.Parse(address);
        return EnableCircuit(a, port, heartBeatInterval, circuitTimeout);
    }

    public Circuit EnableCircuit(IPAddress address, int port, float heartBeatInterval = 5f, float circuitTimeout = 100f)
    {
        Logger.LogDebug("SlMessageSystem.EnableCircuit");

        IPEndPoint endPoint = new IPEndPoint(address, port);
        if (CircuitByEndPoint.ContainsKey(endPoint))
        {
            return CircuitByEndPoint[endPoint];
        }
        Circuit circuit = new Circuit(address, port, this, heartBeatInterval, circuitTimeout);
        CircuitByEndPoint.Add(endPoint, circuit);
        return circuit;
    }

    public void EnqueueMessage(Circuit circuit, byte[] messageBytes)
    {
        OutGoingMessages.Enqueue(new OutgoingMessage(){Circuit = circuit, MessageBytes = messageBytes });
    }


    protected async Task Send(byte[] buffer, Circuit circuit)
    {
        //Logger.LogDebug($"SlMessageSystem.Send: Sending {buffer.Length} bytes...");
        await UdpClient.SendAsync(buffer, buffer.Length, circuit.RemoteEndPoint);
    }
    
    protected void ReceiveData(IAsyncResult ar)
    {
        try
        {
            UdpState state = (UdpState) ar.AsyncState;
            IPEndPoint EndPoint = null;
            byte[] buf = state.Client.EndReceive(ar, ref EndPoint);
            state.Client.BeginReceive(ReceiveData, state);

            if (CircuitByEndPoint.ContainsKey(EndPoint))
            {
                CircuitByEndPoint[EndPoint].ReceiveData(buf);
            }
        }
        catch (ObjectDisposedException e)
        {
            return;
        }
        catch (Exception e)
        {
            Logger.LogError($"SlMessageSystem.ReceiveData: {e}");
        }
    }

    public void Dispose()
    {
        Logger.LogDebug("SlMessagingSystem.Dispose");
        Stop();
    }
}
