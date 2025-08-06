import requests
import json

# Test the chat endpoint with proper payload
url = "http://localhost:5130/api/chat"

# Sample message to send with correct structure
# Using existing demo user ID from seed data
payload = {
    "userId": "user-123",
    "message": "Hello, this is a test message to verify IMessage conversion",
    "systemPrompt": "You are a helpful assistant."
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
