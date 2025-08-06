import requests
import json

# Test the chat endpoint to see if IMessage conversion works and which provider/model is being used
url = "http://localhost:5130/api/chat"

# Sample message to send
payload = {
    "message": "Hello, this is a test message to verify IMessage conversion",
    "conversationId": "test-conversation-456"
}

headers = {
    "Content-Type": "application/json"
}

try:
    response = requests.post(url, data=json.dumps(payload), headers=headers)
    print(f"Status Code: {response.status_code}")
    print(f"Response: {response.text}")
except Exception as e:
    print(f"Error: {e}")
