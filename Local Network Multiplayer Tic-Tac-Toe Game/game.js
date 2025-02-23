document.addEventListener("DOMContentLoaded", function () {
    var cells = document.getElementsByClassName("cell");
    var status = document.getElementById("status");
    var socket = new WebSocket("ws://localhost:8080");

    socket.onopen = function (event) {
        console.log("Connected to server");
    };

    socket.onmessage = function (event) {
        var response = event.data;
        if (response.startsWith("register")) {
            const registresponse = response.substring("register:".length).trim();
            const responseElement = document.getElementById("registername");
            responseElement.innerText = registresponse;
        }
        if (response.startsWith("message")) {
            const messageresponse = response.substring("message:".length).trim();
            const responseElement = document.getElementById("message");
            responseElement.innerText = messageresponse;
        }
        if (response.startsWith("winner")) {
            const winnerresponse = response.substring("winner:".length).trim();
            const responseElement = document.getElementById("message");
            responseElement.innerText = "GAME OVER. " + winnerresponse;
            alert("GAME OVER. " + winnerresponse);
        }
        if (response.startsWith("opponent")) {
            const messageresponse = response.substring("opponent:".length).trim();
            const responseElement = document.getElementById("opponentname");
            responseElement.innerText = messageresponse;
        }
        if (response.startsWith("move")) {
            const moveresponse = response.substring("move:".length).trim();
            var messageArray = moveresponse.split(" ");
            var position = messageArray[0]
            var piece = messageArray[1]
            var cell = document.querySelector('[data-cell="' + position + '"]');
            cell.textContent = piece;
        }
        if (response.startsWith("quit")) {
            const messageresponse = response.substring("quit:".length).trim();
            alert(messageresponse);
            const responseElement = document.getElementById("opponentname");
            responseElement.innerText = "";
            const responseElement1 = document.getElementById("message");
            responseElement1.innerText = "Please find an opponent";
            resetGame()
        }

    };

    socket.onclose = function (event) {
        console.log("Connection closed");
    };

    document.getElementById("registerButton").addEventListener("click", function () {
        var request = "GET /register";
        socket.send(request);
    });

    document.getElementById("FindopponentsButton").addEventListener("click", function () {
        resetGame()
        var request = "GET /pairme";
        socket.send(request);
    });
    document.getElementById("QuitButton").addEventListener("click", function () {
        var request = "GET /quit";
        socket.send(request);
    });


    for (var i = 0; i < cells.length; i++) {
        (function (index) {
            cells[index].addEventListener("click", function () {
                var request = "GET /mymove?move=" + index;
                socket.send(request);
            });
        })(i);
    }

    function resetGame() {
        for (var i = 0; i < cells.length; i++) {
            var cell = document.querySelector('[data-cell="' + i + '"]');
            cell.textContent = '';
        }
    }
});