using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Mcp.Helpers;
using Mcp.Parser;
using UnityEngine;

namespace Mcp.Network
{
    public static class McpTcpServer
    {
        private static TcpListener listener;
        private static bool isRunning;
        private static readonly object Gate = new object();

        public static void Start()
        {
            lock (Gate)
            {
                if (isRunning)
                {
                    Debug.Log($"[A0509 MCP] Already running on port {McpServerConfig.Port}.");
                    return;
                }

                try
                {
                    listener = new TcpListener(IPAddress.Loopback, McpServerConfig.Port);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Start();
                    isRunning = true;
                    _ = ListenLoop();
                    Debug.Log($"[A0509 MCP] Server started on 127.0.0.1:{McpServerConfig.Port}.");
                }
                catch (Exception ex)
                {
                    isRunning = false;
                    listener = null;
                    Debug.LogError($"[A0509 MCP] Failed to start: {ex.Message}");
                }
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                if (!isRunning)
                {
                    return;
                }

                isRunning = false;
                listener?.Stop();
                listener = null;
                Debug.Log("[A0509 MCP] Server stopped.");
            }
        }

        private static async Task ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = HandleClient(client);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        Debug.LogError($"[A0509 MCP] Listen error: {ex.Message}");
                    }
                }
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                client.ReceiveTimeout = McpServerConfig.ConnectionTimeoutMs;
                while (isRunning)
                {
                    string input;
                    try
                    {
                        input = await ReadFrame(stream).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await WriteFrame(stream, McpResponse.Error("read_failed", ex.Message)).ConfigureAwait(false);
                        break;
                    }

                    if (input == null)
                    {
                        break;
                    }

                    string response = input == "ping"
                        ? McpResponse.Success(new { message = "pong" })
                        : await McpCommandParser.Handle(input).ConfigureAwait(false);

                    await WriteFrame(stream, response).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> ReadFrame(NetworkStream stream)
        {
            byte[] header = await ReadExact(stream, 4).ConfigureAwait(false);
            if (header == null)
            {
                return null;
            }

            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length <= 0 || length > McpServerConfig.MaxFrameBytes)
            {
                throw new InvalidDataException($"Invalid frame length: {length}");
            }

            byte[] body = await ReadExact(stream, length).ConfigureAwait(false);
            return body == null ? null : Encoding.UTF8.GetString(body);
        }

        private static async Task WriteFrame(NetworkStream stream, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] header =
            {
                (byte)((body.Length >> 24) & 0xFF),
                (byte)((body.Length >> 16) & 0xFF),
                (byte)((body.Length >> 8) & 0xFF),
                (byte)(body.Length & 0xFF)
            };

            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExact(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }
    }
}
