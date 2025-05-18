package main

import (
	"bufio"
	"log"
	"net/http"
	"os/exec"
	"runtime"

	"github.com/gorilla/websocket"
)

var upgrader = websocket.Upgrader{
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
}

func wsHandler(w http.ResponseWriter, r *http.Request) {
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Println("WebSocket Upgrade error:", err)
		return
	}
	defer conn.Close()

	inputChan := make(chan [2][]byte)

	go func() {
		var inputs [2][]byte
		for i := 0; i < 2; i++ {
			_, msg, err := conn.ReadMessage()
			if err != nil {
				log.Println("Error reading from websocket:", err)
				return
			}
			inputs[i] = append(msg, '\n')
		}
		inputChan <- inputs
	}()

	inputs := <-inputChan

	cmd := exec.Command("cmd", "/C", ".\\TestRunner\\RTProSL-TestRunner.exe")
	stdin, _ := cmd.StdinPipe()
	stdout, _ := cmd.StdoutPipe()
	cmd.Stderr = cmd.Stdout

	err = cmd.Start()
	if err != nil {
		log.Println("Cannot start process:", err)
		conn.WriteMessage(websocket.TextMessage, []byte("[Error: cannot start test runner]"))
		return
	}

	stdin.Write(inputs[0])
	stdin.Write(inputs[1])
	stdin.Close()

	reader := bufio.NewReader(stdout)
	for {
		line, err := reader.ReadBytes('\n')
		if err != nil {
			break
		}
		conn.WriteMessage(websocket.TextMessage, line)
	}

	cmd.Wait()
	conn.WriteMessage(websocket.TextMessage, []byte("=== Test finished ==="))
}

func openBrowser(url string) {
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		chromePath := "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
		cmd = exec.Command(chromePath, "--incognito", url)
	case "darwin": // macOS
		cmd = exec.Command("open", "-a", "Google Chrome", "--args", "--incognito", url)
	default: // Linux
		cmd = exec.Command("chromium", "--incognito", url)
	}
	_ = cmd.Start()
}

func main() {
	http.HandleFunc("/ws", wsHandler)
	http.Handle("/", http.FileServer(http.Dir(".")))

	url := "http://localhost:8080"
	log.Println("Server listening on", url)

	go openBrowser(url)

	log.Fatal(http.ListenAndServe(":8080", nil))
}
