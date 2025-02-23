using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Reflection;
using Microsoft.Win32;
using System.IO;

class GameSever
{
    private static readonly object lockObject = new object();
    private static readonly ConcurrentDictionary<string, Player> players = new ConcurrentDictionary<string, Player>();
    private static readonly ConcurrentDictionary<string, Game> games = new ConcurrentDictionary<string, Game>();
    private static int playerIdCounter = 0;
    private static int gameIdCounter = 0;
    private static bool IsRegister = false;

    static async Task Main()
    {
        IPAddress ipAddress = IPAddress.Any; // Listen to all available IP addresses
        int port = 8080; // server port
        TcpListener listener = new TcpListener(ipAddress, port);
        listener.Start();
        Console.WriteLine("WebSocket server started. Listening on port 8080...");

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Console.WriteLine("Client connected: {client.Client.RemoteEndPoint}");
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        if (client == null || !client.Connected)
        {
            Console.WriteLine("Invalid or disconnected client.");
            return;
        }
        try
        {
            using (NetworkStream stream = client.GetStream())
            {
                // Perform WebSocket handshake
                WebSocket websocket = await WebSocketHandshake(stream);
                if (websocket == null)
                {
                    Console.WriteLine("WebSocket handshake failed.Start telnet.");
                    var requestBuilder = new StringBuilder();
                    var buffer = new byte[1024];
                    var player = new Player(null);
                    while (true)
                    {
                        try
                        {
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            requestBuilder.Append(requestData);
                            Console.WriteLine($"{requestData}");
                            if (bytesRead == 2)
                            {
                                Console.WriteLine($"{requestBuilder}");
                                var request = requestBuilder.ToString().Trim();
                                var response = ProcessRequest(request, player);
                                var responseBytes = Encoding.UTF8.GetBytes(response + "\n\r");
                                stream.Write(responseBytes, 0, responseBytes.Length);
                                if (player.IsWaitting)
                                {
                                    while (player.IsWaitting) { }
                                    var success = Encoding.UTF8.GetBytes("Successfully matched,your opponent is: " + player.Opponent.Username + "\n\r");
                                    stream.Write(success, 0, success.Length);
                                }
                                if (player.PairedGameId != null)
                                {
                                    games.TryGetValue(player.PairedGameId, out var game);

                                    if (player.IsPaired == true && game.IsActive == true && game.LastMoveBy == player.Username)
                                    {
                                        var wait = "Not your turn. Wait for your opponent\r\n";
                                        stream.Write(Encoding.UTF8.GetBytes(wait));
                                        while (game.LastMoveBy == player.Username && game.Winner == null && !game.draw)
                                        {
                                            if (!player.IsPaired)
                                            {
                                                stream.Write(Encoding.UTF8.GetBytes("Your opponent has left\r\n"));
                                                player.quitgame();
                                                break;
                                            }
                                        }
                                        if (player.IsPaired)
                                        {
                                            var Lastmove = game.GetOpponentMove(player);
                                            stream.Write(Encoding.UTF8.GetBytes("The opponent move is: " + Lastmove + "\r\n"));
                                        }
                                    }
                                    game.checkwin();
                                    if (game.Winner != null)
                                    {
                                        if (game.Winner.StartsWith(player.Username))
                                        {
                                            var winner = "The winner is " + player.Username;
                                            stream.Write(Encoding.UTF8.GetBytes(winner + "\r\n"));
                                            Closegame(game);
                                        }
                                        else
                                        {
                                            var winner = "The winner is " + player.Opponent.Username;
                                            stream.Write(Encoding.UTF8.GetBytes(winner + "\r\n"));
                                        }
                                        player.quitgame();
                                        stream.Write(Encoding.UTF8.GetBytes("Your opponent has left\r\n"));
                                    }
                                    var draw = game.checkdraw();
                                    if (draw && game.Winner == null)
                                    {
                                        stream.Write(Encoding.UTF8.GetBytes("DRAW! There is no winner\r\n"));
                                        stream.Write(Encoding.UTF8.GetBytes("Your opponent has left\r\n"));
                                        Closegame(game);
                                        player.quitgame();
                                    }

                                    if (game.LastMoveBy != player.Username && game.Winner == null && !draw)
                                    {
                                        var yourturn = "It's your turn";
                                        stream.Write(Encoding.UTF8.GetBytes(yourturn + "\r\n"));
                                    }
                                }
                                requestBuilder.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Invalid endpoint");
                            requestBuilder.Clear();
                            continue;
                        }

                    }
                }
                else
                {
                    Console.WriteLine("WebSocket handshake successful. Ready for communication.");
                    var player = new Player(null);

                    while (websocket.State == WebSocketState.Open)
                    {
                        byte[] buffer = new byte[1024];
                        WebSocketReceiveResult result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                            Console.WriteLine("WebSocket connection closed.");
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            var response = ProcessRequest(message, player);
                            var responseBytes = Encoding.UTF8.GetBytes(response);
                            await websocket.SendAsync(Encoding.UTF8.GetBytes(response), WebSocketMessageType.Text, true, CancellationToken.None);
                            if (player.IsWaitting)
                            {
                                while (player.IsWaitting) { }
                                var success = "message:Successfully matched";
                                await websocket.SendAsync(Encoding.UTF8.GetBytes(success), WebSocketMessageType.Text, true, CancellationToken.None);
                                var opp = "opponent:" + player.Opponent.Username;
                                await websocket.SendAsync(Encoding.UTF8.GetBytes(opp), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            if (player.PairedGameId != null)
                            {
                                games.TryGetValue(player.PairedGameId, out var game);

                                if (player.IsPaired == true && game.IsActive == true && game.LastMoveBy == player.Username)
                                {
                                    var wait = "message:Not your turn. Wait for your opponent";
                                    await websocket.SendAsync(Encoding.UTF8.GetBytes(wait), WebSocketMessageType.Text, true, CancellationToken.None);
                                    while (game.LastMoveBy == player.Username && game.Winner == null && !game.draw)
                                    {
                                        if (!player.IsPaired)
                                        {
                                            await websocket.SendAsync(Encoding.UTF8.GetBytes("quit: Your opponent has left"), WebSocketMessageType.Text, true, CancellationToken.None);
                                            player.quitgame();
                                            break;
                                        }
                                    }
                                    if (player.IsPaired)
                                    {
                                        var Lastmove = game.GetOpponentMove(player);
                                        await websocket.SendAsync(Encoding.UTF8.GetBytes(Lastmove), WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                }
                                game.checkwin();
                                if (game.Winner != null)
                                {
                                    if (game.Winner.StartsWith(player.Username))
                                    {
                                        var winner = "winner:The winner is " + player.Username;
                                        await websocket.SendAsync(Encoding.UTF8.GetBytes(winner), WebSocketMessageType.Text, true, CancellationToken.None);
                                        Closegame(game);
                                    }
                                    else
                                    {
                                        var winner = "winner:The winner is " + player.Opponent.Username;
                                        await websocket.SendAsync(Encoding.UTF8.GetBytes(winner), WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                    player.quitgame();
                                    await websocket.SendAsync(Encoding.UTF8.GetBytes("opponent:Your opponent has left"), WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                                var draw = game.checkdraw();

                                if (draw && game.Winner == null)
                                {
                                    await websocket.SendAsync(Encoding.UTF8.GetBytes("winner:DRAW! There is no winner"), WebSocketMessageType.Text, true, CancellationToken.None);
                                    await websocket.SendAsync(Encoding.UTF8.GetBytes("opponent:Your opponent has left"), WebSocketMessageType.Text, true, CancellationToken.None);
                                    Closegame(game);
                                    QuitGame(player, player.PairedGameId);
                                }
                                if (game.LastMoveBy != player.Username && game.Winner == null && !draw)
                                {
                                    var yourturn = "message:It's your turn";
                                    await websocket.SendAsync(Encoding.UTF8.GetBytes(yourturn), WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            client.Close();
        }
    }

    private static async Task<WebSocket> WebSocketHandshake(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        StringBuilder requestBuilder = new StringBuilder();
        string eom = "\r\n\r\n"; // End flag of the message

        while (true)
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                // The client has disconnected
                return null;
            }

            string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Received handshake request: \"{requestData}\"");

            if (requestData == "\r\n")
            {
                return null;
            }

            requestBuilder.Append(requestData);
            string request = requestBuilder.ToString();

            int eomIndex = request.IndexOf(eom);
            if (eomIndex > -1)
            {
                string handshakeRequest = request.Substring(0, eomIndex + eom.Length);
                Console.WriteLine($"Performing WebSocket handshake.");

                // Perform WebSocket handshake
                WebSocket websocket = await WebSocketUpgrade(stream, handshakeRequest);
                return websocket;
            }
        }
    }

    private static async Task<WebSocket> WebSocketUpgrade(NetworkStream stream, string handshakeRequest)
    {
        const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        string[] headers = handshakeRequest.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        string key = null;

        foreach (string header in headers)
        {
            if (header.StartsWith("Sec-WebSocket-Key:"))
            {
                key = header.Substring("Sec-WebSocket-Key:".Length).Trim();
                break;
            }
        }
        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine("Invalid WebSocket handshake request. Missing Sec-WebSocket-Key header.");
            return null;
        }

        string acceptKey = ComputeWebSocketHandshakeAcceptKey(key, magicString);
        string response = $"HTTP/1.1 101 Switching Protocols\r\n" +
                          $"Upgrade: websocket\r\n" +
                          $"Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
                          $"\r\n";

        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
        Console.WriteLine("WebSocket handshake response sent.");

        // Return WebSocket object for subsequent communication
        return WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(120));
    }

    private static string ComputeWebSocketHandshakeAcceptKey(string key, string magicString)
    {
        string concatenated = key + magicString;
        byte[] sha1HashBytes;

        using (SHA1 sha1 = SHA1.Create())
        {
            sha1HashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        }

        string acceptKey = Convert.ToBase64String(sha1HashBytes);
        return acceptKey;
    }


    private static string ProcessRequest(string request, Player player)
    {
        var segments = request.Split(' ');
        var method = segments[0];
        var path = segments[1];

        if (method == "GET")
        {
            if (path == "/register")
            {
                return RegisterPlayer(player);
            }
            else if (path.StartsWith("/pairme"))
            {
                return PairPlayer(player);
            }
            else if (path.StartsWith("/mymove?"))
            {
                var move = GetQueryParam(path, "move");
                return UpdatePlayerMove(player, move);
            }
            else if (path.StartsWith("/theirmove"))
            {
                return GetOpponentMove(player);
            }
            else if (path.StartsWith("/quit"))
            {
                return QuitGame(player, player.PairedGameId);
            }
            // Implement other GET endpoints
        }

        return "Invalid endpoint";
    }

    private static string RegisterPlayer(Player player)
    {
        if (player.Username == null)
        {
            var username = $"Player{Interlocked.Increment(ref playerIdCounter)}";
            player.Username = username;
            players.TryAdd(username, player);
            player.Register();
            return "register:" + player.Username;
        }
        else
        {
            return $"message:You have already registered, Your username is {player.Username}";
        }

    }

    private static string PairPlayer(Player player)
    {
        if (player.Username != null)
        {
            var waitingPlayer = players.Values.FirstOrDefault(p => p != player && p.IsWaitting == true);
            if (waitingPlayer != null)
            {
                Console.WriteLine($"Game start: {player.Username} VS {waitingPlayer.Username}");
                var gameId = $"Game{Interlocked.Increment(ref gameIdCounter)}";
                var game = new Game(gameId, player, waitingPlayer);
                game.Gamestart();
                games.TryAdd(gameId, game);
                waitingPlayer.Pair(gameId, player);
                player.Pair(gameId, waitingPlayer);
                return "opponent:" + player.Opponent.Username;
            }
            else
            {
                player.SetWaiting();
                return $"message:Waiting for another player to join the game";
            }
        }
        return $"message:Please register first";
    }

    private static string UpdatePlayerMove(Player player, string move)
    {
        if (player.IsRegister)
        {
            if (player.IsPaired)
            {
                if (games.TryGetValue(player.PairedGameId, out var game))
                {
                    if (game.IsActive)
                    {
                        if (game.IsPlayerTurn(player.Username))
                        {
                            string response = game.UpdatePlayerMove(player.Username, move);
                            return response;
                        }
                        else
                        {
                            return "message:Not your turn";
                        }
                    }
                    else
                    {
                        return "Game not in progress";
                    }
                }
            }
            else
            {
                return "message:Please find an opponent ";
            }
        }
        else
        {
            return "message:Please register first ";
        }
        return "message:error ";
    }

    private static string GetOpponentMove(Player player)
    {
        if (games.TryGetValue(player.PairedGameId, out var game))
        {
            if (game.IsActive)
            {
                var opponentMove = game.GetOpponentMove(player);
                return opponentMove ?? "Waiting for opponent's move";
            }
            else
            {
                return "Game not in progress";
            }
        }

        return "Invalid game ID";
    }

    private static void Closegame(Game game)
    {
        Game removedgame = null;
        games.TryRemove(game.GameId, out removedgame);
    }

    private static string QuitGame(Player player, string gameId)
    {
        if (player.Opponent != null)
        {
            player.Opponent.Unpair();
        }
        player.quitgame();
        return "quit:Game quit successfully";
    }

    private static string GetQueryParam(string path, string paramName)
    {
        var startIndex = path.IndexOf(paramName + "=") + paramName.Length + 1;
        var endIndex = path.IndexOf('&', startIndex);
        if (endIndex == -1)
            endIndex = path.Length;
        return path.Substring(startIndex, endIndex - startIndex);
    }
}

public class Player
{
    public string Username;

    public bool IsRegister { get; private set; }
    public bool IsPaired { get; private set; }
    public bool IsWaitting { get; private set; }
    public bool IsInGame { get; private set; }
    public string? PairedGameId { get; private set; }
    public Player Opponent { get; private set; }



    public Player(string username)
    {
        Username = username;
        IsRegister = false;
        IsPaired = false;
        IsWaitting = false;
        PairedGameId = null;
        Opponent = null;
    }
    public void Register()
    {
        IsRegister = true;
    }

    public void Pair(string gameId, Player opponent)
    {
        IsPaired = true;
        IsWaitting = false;
        PairedGameId = gameId;
        Opponent = opponent;
    }

    public void SetWaiting()
    {
        IsWaitting = true;
        PairedGameId = null;
        Opponent = null;
    }

    public void Unpair()
    {
        IsPaired = false;
        PairedGameId = null;
    }
    public void quitgame()
    {
        IsPaired = false;
        IsWaitting = false;
        PairedGameId = null;
        Opponent = null;
    }
}

public class Game
{
    public string GameId { get; }
    public Player Player1 { get; }
    public Player Player2 { get; }
    public string Player1_piece { get; private set; }
    public string Player2_piece { get; private set; }
    public string LastMovePlayer1 { get; private set; }
    public string LastMovePlayer2 { get; private set; }
    public string LastMoveBy { get; private set; }
    public string Winner { get; private set; }
    public bool IsActive { get; private set; }
    public bool draw { get; private set; }
    public int[] checkerboard { get; private set; }



    public Game(string gameId, Player player1, Player player2)
    {
        GameId = gameId;
        Player1 = player1;
        Player2 = player2;
        LastMovePlayer1 = "NO Move";
        LastMovePlayer2 = "NO Move";
        IsActive = false;
        LastMoveBy = player2.Username;
        checkerboard = new int[9];
        Winner = null;
        for (int i = 0; i < 9; i++)
        {
            checkerboard[i] = 0;
        }
        draw = false;

    }
    public void Gamestart()
    {
        IsActive = true;
    }

    public string UpdatePlayerMove(string username, string move)
    {


        int position = 0;
        for (int i = 0; i < 9; i++)
        {
            if (i.ToString() == move)
            {
                position = i;
            }
        }

        if (checkerboard[position] == 0)
        {

            if (Player1.Username == username)
            {
                LastMovePlayer1 = "move:" + move + " X";
                checkerboard[position] = 1;
                LastMoveBy = username;
                return "move:" + move + " X";
            }
            else if (Player2.Username == username)
            {
                LastMovePlayer2 = "move:" + move + " O";
                checkerboard[position] = 2;
                LastMoveBy = username;
                return "move:" + move + " O";
            }

        }
        else
        { return "message:" + "There is already chess piece here!"; }
        return "message:" + "erro";


    }

    public bool IsPlayerTurn(string username)
    {
        if (!IsActive)
            return false;
        if (LastMoveBy != username)
            return true;
        if (LastMoveBy == username)
            return false;
        return false;
    }

    public string GetOpponentMove(Player player)
    {
        if (Player1.Username == player.Username)
        {
            return LastMovePlayer2;
        }
        else if (Player2.Username == player.Username)
        {
            return LastMovePlayer1;
        }
        return "NO MOVE";
    }

    public string GetGameStatus()
    {
        if (IsActive)
        {
            return $"\n\rGame ID: {GameId}\n\rStatus: In progress\n\rPlayer 1: {Player1.Username}\n\rPlayer 2: {Player2.Username}\n\rLast move by Player 1: {LastMovePlayer1}\n\rLast move by Player 2: {LastMovePlayer2}";
        }
        else
        {
            return $"Game ID: {GameId}\n\rStatus: Waiting\n\rPlayer 1: {Player1}\n\rWaiting for another player to join the game";
        }
    }
    public void close(Game game)
    {
    }
    public void checkwin()
    {
        for (int i = 0; i < 7; i = i + 3)
        {
            if (checkerboard[i] == checkerboard[i + 1] && checkerboard[i + 1] == checkerboard[i + 2] && checkerboard[i] != 0)
            {
                if (checkerboard[i] == 1)
                {
                    Winner = Player1.Username;
                }
                if (checkerboard[i] == 2)
                {
                    Winner = Player2.Username;
                }
            }
        }
        for (int i = 0; i < 3; i++)
        {
            if (checkerboard[i] == checkerboard[i + 3] && checkerboard[i + 3] == checkerboard[i + 6] && checkerboard[i] != 0)
            {
                if (checkerboard[i] == 1)
                {
                    Winner = Player1.Username;
                }
                if (checkerboard[i] == 2)
                {
                    Winner = Player2.Username;
                }
            }
        }
        if (checkerboard[0] == checkerboard[4] && checkerboard[4] == checkerboard[8] && checkerboard[0] != 0)
        {
            if (checkerboard[0] == 1)
            {
                Winner = Player1.Username;
            }
            if (checkerboard[0] == 2)
            {
                Winner = Player2.Username;
            }
        }
        if (checkerboard[2] == checkerboard[4] && checkerboard[4] == checkerboard[6] && checkerboard[2] != 0)
        {
            if (checkerboard[0] == 1)
            {
                Winner = Player1.Username;
            }
            if (checkerboard[0] == 2)
            {
                Winner = Player2.Username;
            }
        }
    }
    public bool checkdraw()
    {
        for (int i = 0; i < 9; i++)
        {
            if (checkerboard[i] == 0)
            {
                draw = false;
                return false;
            }
        }
        draw = true;
        return true;
    }
}
