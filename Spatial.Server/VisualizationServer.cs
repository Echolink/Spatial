using Fleck;
using Newtonsoft.Json;

namespace Spatial.Server;

/// <summary>
/// WebSocket server that broadcasts simulation state to Unity clients
/// </summary>
public class VisualizationServer : IDisposable
{
    private WebSocketServer? _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _clientLock = new();
    private bool _isRunning;
    
    /// <summary>
    /// Start the WebSocket server on the specified port
    /// </summary>
    /// <param name="port">Port to listen on (default: 8181)</param>
    public void Start(int port = 8181)
    {
        if (_isRunning)
        {
            Console.WriteLine("Visualization server is already running");
            return;
        }
        
        FleckLog.Level = LogLevel.Info;
        
        // Redirect Fleck logs to console
        FleckLog.LogAction = (level, message, ex) =>
        {
            if (level == LogLevel.Error)
            {
                Console.WriteLine($"[WebSocket Error] {message}");
                if (ex != null)
                    Console.WriteLine($"  Exception: {ex.Message}");
            }
        };
        
        _server = new WebSocketServer($"ws://0.0.0.0:{port}");
        
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                lock (_clientLock)
                {
                    _clients.Add(socket);
                }
                Console.WriteLine($"[Viz Server] Unity client connected from {socket.ConnectionInfo.ClientIpAddress}");
                Console.WriteLine($"[Viz Server] Total clients: {_clients.Count}");
            };
            
            socket.OnClose = () =>
            {
                lock (_clientLock)
                {
                    _clients.Remove(socket);
                }
                Console.WriteLine($"[Viz Server] Unity client disconnected");
                Console.WriteLine($"[Viz Server] Total clients: {_clients.Count}");
            };
            
            socket.OnError = (ex) =>
            {
                Console.WriteLine($"[Viz Server] WebSocket error: {ex.Message}");
            };
            
            socket.OnMessage = (message) =>
            {
                // Handle messages from Unity client if needed (e.g., commands)
                Console.WriteLine($"[Viz Server] Received message from client: {message}");
            };
        });
        
        _isRunning = true;
        Console.WriteLine($"[Viz Server] Started on ws://localhost:{port}");
        Console.WriteLine($"[Viz Server] Waiting for Unity client connections...");
    }
    
    /// <summary>
    /// Broadcast simulation state to all connected clients
    /// </summary>
    public void BroadcastState(SimulationState state)
    {
        if (!_isRunning || _clients.Count == 0)
            return;
        
        try
        {
            var json = JsonConvert.SerializeObject(state, Formatting.None);
            
            lock (_clientLock)
            {
                foreach (var client in _clients.ToList())
                {
                    try
                    {
                        if (client.IsAvailable)
                        {
                            client.Send(json);
                        }
                        else
                        {
                            _clients.Remove(client);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Viz Server] Failed to send to client: {ex.Message}");
                        _clients.Remove(client);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viz Server] Failed to broadcast state: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Check if any clients are connected
    /// </summary>
    public bool HasClients()
    {
        lock (_clientLock)
        {
            return _clients.Count > 0;
        }
    }
    
    /// <summary>
    /// Get the number of connected clients
    /// </summary>
    public int ClientCount
    {
        get
        {
            lock (_clientLock)
            {
                return _clients.Count;
            }
        }
    }
    
    /// <summary>
    /// Stop the server and disconnect all clients
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;
        
        Console.WriteLine("[Viz Server] Stopping...");
        
        lock (_clientLock)
        {
            foreach (var client in _clients.ToList())
            {
                try
                {
                    client.Close();
                }
                catch { }
            }
            _clients.Clear();
        }
        
        _server?.Dispose();
        _isRunning = false;
        
        Console.WriteLine("[Viz Server] Stopped");
    }
    
    public void Dispose()
    {
        Stop();
    }
}
