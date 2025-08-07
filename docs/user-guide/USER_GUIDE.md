# User Guide - AI Chat Application

## Getting Started

Welcome to the AI Chat Application! This guide will help you understand how to use the application effectively.

## Application Overview

The AI Chat Application is a modern web-based chat interface that allows you to have conversations with an AI assistant. The application provides real-time messaging, conversation management, and a clean, intuitive user interface.

## Accessing the Application

### Development Environment
- **URL**: http://localhost:5173
- **Requirements**: Modern web browser with JavaScript enabled
- **Compatibility**: Chrome, Firefox, Safari, Edge (latest versions)

## Main Interface

### Layout Overview

The application consists of three main areas:

1. **Sidebar** (Left): Conversation list and navigation
2. **Chat Area** (Center): Message display and conversation view
3. **Input Area** (Bottom): Message composition and sending

### Sidebar Features

**New Chat Creation**
- Text input field: "Start a new conversation..."
- "New Chat" button (enabled when text is entered)
- User profile indicator (Demo User)

**Conversation List**
- All previous conversations are listed
- Each conversation shows:
  - First message preview
  - Last AI response preview
  - Timestamp of last activity
  - Delete button for conversation removal

**Navigation**
- Click any conversation to open it
- Active conversation is highlighted
- Conversations are ordered by most recent activity

## Creating a New Conversation

### Step-by-Step Process

1. **Enter Your Message**
   - Click in the text field: "Start a new conversation..."
   - Type your initial message or question
   - The "New Chat" button will become enabled

2. **Start the Conversation**
   - Click the "New Chat" button
   - Your message will be sent immediately
   - A new conversation will be created and added to the sidebar

3. **Receive AI Response**
   - The AI will process your message
   - Response will stream in real-time
   - Full conversation will be saved automatically

## Chatting in Existing Conversations

### Sending Messages

1. **Select a Conversation**
   - Click any conversation from the sidebar
   - The conversation will open in the main chat area

2. **Compose Your Message**
   - Click in the text input at the bottom
   - Type your message
   - Use formatting as needed

3. **Send Your Message**
   - Press **Enter** to send
   - Or click the send button
   - Use **Shift+Enter** for new lines within a message

### Message Features

**Message Display**
- Your messages appear on the right with a user icon
- AI responses appear on the left with an AI icon
- All messages show timestamps
- Messages are displayed in chronological order

**Real-time Updates**
- Messages appear instantly
- AI responses stream as they're generated
- Connection status shown at bottom ("Connected")

## Conversation Management

### Viewing Conversations

**Conversation Header**
- Shows conversation title (based on first message)
- Displays total message count
- Provides conversation context

**Message History**
- All messages in a conversation are preserved
- Scroll to view older messages
- Chronological ordering maintained

### Deleting Conversations

1. **Locate Delete Button**
   - Each conversation in sidebar has a delete button (trash icon)
   - Button appears on hover or is always visible

2. **Confirm Deletion**
   - Click the delete button
   - Conversation will be removed immediately
   - Action cannot be undone

## Understanding AI Responses

### Response Types

The AI can provide various types of responses:

- **Conversational**: Natural dialogue and Q&A
- **Informational**: Explanations and overviews
- **Task-oriented**: Help with specific requests
- **Structured**: Lists, plans, and organized information

### AI Capabilities

According to the application testing, the AI supports:

- Natural language conversations
- Context retention within sessions
- Multi-turn dialogue
- Question answering
- Task assistance (writing, analysis, planning)
- Code explanation and help
- Structured output formatting

## Connection and Status

### Connection Status

**Connected State**
- Green "Connected" indicator at bottom
- Real-time messaging enabled
- Instant message delivery

**Connection Issues**
- If connection drops, try refreshing the page
- Check your internet connection
- Ensure both client and server are running

### Performance Expectations

**Response Times**
- Message sending: Instant
- AI response start: 1-2 seconds
- Full response: 3-5 seconds (varies by complexity)

## Tips for Best Experience

### Message Composition

1. **Be Clear and Specific**
   - State your questions clearly
   - Provide context when needed
   - Use specific examples

2. **Use Proper Formatting**
   - Use Shift+Enter for line breaks
   - Keep messages organized
   - Break complex requests into parts

3. **Conversation Flow**
   - Build on previous messages
   - Reference earlier parts of conversation
   - Ask follow-up questions

### Managing Conversations

1. **Organization**
   - Create separate conversations for different topics
   - Use descriptive first messages
   - Delete old conversations when no longer needed

2. **Context Awareness**
   - AI remembers conversation context
   - Reference previous messages naturally
   - Build complex discussions over multiple turns

## Troubleshooting

### Common Issues

**New Chat Button Disabled**
- Ensure you've entered text in the input field
- Text field must contain at least one character

**Messages Not Sending**
- Check connection status at bottom
- Refresh page if "Connected" status is missing
- Verify server is running

**AI Not Responding**
- Wait a few seconds for processing
- Check connection status
- Try sending a simpler message

**Conversation Not Loading**
- Refresh the page
- Check if conversation still exists in sidebar
- Try selecting a different conversation first

### Getting Help

If you encounter issues:

1. **Check Connection**: Ensure "Connected" status is shown
2. **Refresh Page**: Often resolves temporary issues
3. **Try Again**: Some issues are temporary
4. **Check Console**: Browser developer tools may show errors

## Privacy and Data

### Data Storage

- Conversations are stored locally in the application database
- Messages persist between sessions
- Conversation history maintained until manually deleted

### Privacy Considerations

- Messages are processed by the AI service
- Conversation data is stored on the application server
- Delete conversations to remove data permanently

## Advanced Features

### Keyboard Shortcuts

- **Enter**: Send message
- **Shift+Enter**: New line in message
- **Ctrl+/**: Focus message input (may vary by browser)

### Real-time Features

- **Live Typing**: Messages appear as you send them
- **Streaming Responses**: AI responses appear as generated
- **Connection Monitoring**: Status indicator shows connection health

## Conclusion

The AI Chat Application provides a powerful, user-friendly interface for AI conversations. With its real-time messaging, conversation management, and intuitive design, you can effectively communicate with the AI assistant for various tasks and inquiries.

For additional help or feature requests, contact the development team or refer to the technical documentation.
