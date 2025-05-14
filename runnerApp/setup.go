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

	// اجرای تست‌رانر
	cmd := exec.Command("cmd", "/C", ".\\TestRunner\\RTProSL-TestRunner.exe")

	stdin, _ := cmd.StdinPipe()
	stdout, _ := cmd.StdoutPipe()
	cmd.Stderr = cmd.Stdout

	err = cmd.Start()
	if err != nil {
		log.Println("Cannot start process:", err)
		return
	}

	// ✅ دادن ورودی‌ها به تست‌رانر
	go func() {
		// این مقادیر رو می‌تونی از کلاینت هم بفرستی
		_, _ = stdin.Write([]byte("3\n"))
		_, _ = stdin.Write([]byte("1\n"))
		stdin.Close()
	}()

	// ✅ خواندن خروجی لحظه‌ای و فرستادن از طریق WebSocket
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
		cmd = exec.Command("rundll32", "url.dll,FileProtocolHandler", url)
	case "darwin":
		cmd = exec.Command("open", url)
	default: // linux
		cmd = exec.Command("xdg-open", url)
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
