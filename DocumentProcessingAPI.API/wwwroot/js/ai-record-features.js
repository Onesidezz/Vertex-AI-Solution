// AI Record Features - Summary and Ask AI functionality
// Using jQuery for consistency with the main application

// Global variables
let currentRecordUri = null;

// Typewriter effect for AI responses
function typeWriterForAnalytics(text, element, speed) {
    if (!text) return;

    var i = 0;
    element.html('');

    function type() {
        if (i < text.length) {
            element.append(text.charAt(i));
            i++;
            setTimeout(type, speed);
        }
    }
    type();
}

// Document ready
$(document).ready(function() {
    initializeAIFeatures();
});

function initializeAIFeatures() {
    // AI Summary button click handler
    $('.ai-summary-btn').on('click', function(e) {
        e.preventDefault();
        e.stopPropagation();

        const recordUri = $(this).data('uri');
        const recordTitle = $(this).closest('tr').find('.record-name-link').text().trim();

        showSummaryModal(recordUri, recordTitle);
    });

    // AI Ask button click handler
    $('.ai-ask-btn').on('click', function(e) {
        e.preventDefault();
        e.stopPropagation();

        const recordUri = $(this).data('uri');
        const recordTitle = $(this).closest('tr').find('.record-name-link').text().trim();

        showAskModal(recordUri, recordTitle);
    });

    // Summary modal close button
    $('.ai-summary-modal-close').on('click', function() {
        $('#aiSummaryModal').fadeOut(200);
    });

    // Ask modal close button
    $('.ai-ask-modal-close').on('click', function() {
        $('#aiAskModal').fadeOut(200);
        // Clear chat history
        $('#aiAskChatContainer').html('');
    });

    // Close modals when clicking outside
    $(window).on('click', function(event) {
        if ($(event.target).hasClass('ai-summary-modal')) {
            $('#aiSummaryModal').fadeOut(200);
        }
        if ($(event.target).hasClass('ai-ask-modal')) {
            $('#aiAskModal').fadeOut(200);
            $('#aiAskChatContainer').html('');
        }
    });

    // Ask AI - Send button click
    $('#aiAskSendBtn').on('click', function() {
        sendAskQuestion();
    });

    // Ask AI - Enter key press
    $('#aiAskInput').on('keypress', function(e) {
        if (e.which === 13) { // Enter key
            sendAskQuestion();
        }
    });
}

// Show Summary Modal
function showSummaryModal(recordUri, recordTitle) {
    const modal = $('#aiSummaryModal');

    // Set record info
    $('#aiSummaryRecordInfo').html(`
        <div><strong>Record URI:</strong> ${recordUri}</div>
        <div><strong>Title:</strong> ${recordTitle}</div>
    `);

    // Show loading state
    $('#aiSummaryLoading').show();
    $('#aiSummaryText').hide();

    // Show modal
    modal.fadeIn(200);

    // Call API to get summary
    $.ajax({
        url: `/api/RecordAI/summary/${recordUri}`,
        method: 'GET',
        success: function(response) {
            // Hide loading
            $('#aiSummaryLoading').hide();

            // Show summary with typewriter effect
            const summaryElement = $('#aiSummaryText');
            summaryElement.show();

            // Use typewriter effect (20ms per character for smooth effect)
            typeWriterForAnalytics(response.summary, summaryElement, 20);
        },
        error: function(xhr, status, error) {
            // Hide loading
            $('#aiSummaryLoading').hide();

            // Show error message
            const summaryElement = $('#aiSummaryText');
            summaryElement.show();

            let errorMessage = 'Failed to generate summary. Please try again.';
            if (xhr.responseJSON && xhr.responseJSON.message) {
                errorMessage = xhr.responseJSON.message;
            }

            summaryElement.html(`<p style="color: #d32f2f;"><i class="bi bi-exclamation-triangle"></i> ${errorMessage}</p>`);
        }
    });
}

// Show Ask AI Modal
function showAskModal(recordUri, recordTitle) {
    currentRecordUri = recordUri;

    const modal = $('#aiAskModal');

    // Set record info
    $('#aiAskRecordInfo').html(`
        <strong>Record URI:</strong> ${recordUri} | <strong>Title:</strong> ${recordTitle}
    `);

    // Clear previous chat
    $('#aiAskChatContainer').html('');
    $('#aiAskInput').val('');

    // Show modal
    modal.fadeIn(200);

    // Focus on input
    setTimeout(() => {
        $('#aiAskInput').focus();
    }, 300);
}

// Send Ask Question
function sendAskQuestion() {
    const question = $('#aiAskInput').val().trim();

    if (!question) {
        return;
    }

    // Disable input and button during request
    $('#aiAskInput').prop('disabled', true);
    $('#aiAskSendBtn').prop('disabled', true);

    // Add user message to chat
    addMessageToChat('user', question);

    // Clear input
    $('#aiAskInput').val('');

    // Add loading indicator for AI response
    const loadingId = 'ai-loading-' + Date.now();
    $('#aiAskChatContainer').append(`
        <div class="ai-ask-message ai" id="${loadingId}">
            <div class="ai-ask-message-content">
                <i class="bi bi-hourglass-split"></i> Thinking...
            </div>
        </div>
    `);

    // Scroll to bottom
    scrollChatToBottom();

    // Call API
    $.ajax({
        url: `/api/RecordAI/ask/${currentRecordUri}`,
        method: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({
            question: question
        }),
        success: function(response) {
            // Remove loading indicator
            $(`#${loadingId}`).remove();

            // Add AI response with typewriter effect
            addAIMessageWithTypewriter(response.answer);

            // Re-enable input and button
            $('#aiAskInput').prop('disabled', false);
            $('#aiAskSendBtn').prop('disabled', false);
            $('#aiAskInput').focus();
        },
        error: function(xhr, status, error) {
            // Remove loading indicator
            $(`#${loadingId}`).remove();

            // Add error message
            let errorMessage = 'Failed to get answer. Please try again.';
            if (xhr.responseJSON && xhr.responseJSON.message) {
                errorMessage = xhr.responseJSON.message;
            }

            addMessageToChat('ai', `<span style="color: #d32f2f;"><i class="bi bi-exclamation-triangle"></i> ${errorMessage}</span>`);

            // Re-enable input and button
            $('#aiAskInput').prop('disabled', false);
            $('#aiAskSendBtn').prop('disabled', false);
            $('#aiAskInput').focus();
        }
    });
}

// Add message to chat
function addMessageToChat(sender, message) {
    const chatContainer = $('#aiAskChatContainer');

    const messageHtml = `
        <div class="ai-ask-message ${sender}">
            <div class="ai-ask-message-content">${message}</div>
        </div>
    `;

    chatContainer.append(messageHtml);
    scrollChatToBottom();
}

// Add AI message with typewriter effect
function addAIMessageWithTypewriter(message) {
    const chatContainer = $('#aiAskChatContainer');
    const messageId = 'ai-msg-' + Date.now();

    // Add empty AI message container
    chatContainer.append(`
        <div class="ai-ask-message ai" id="${messageId}">
            <div class="ai-ask-message-content"></div>
        </div>
    `);

    // Get the message content element
    const messageElement = $(`#${messageId} .ai-ask-message-content`);

    // Use typewriter effect (15ms per character for conversational feel)
    typeWriterForAnalytics(message, messageElement, 15);

    // Scroll as text is being typed
    const scrollInterval = setInterval(() => {
        scrollChatToBottom();
    }, 100);

    // Stop scrolling after typewriter completes
    setTimeout(() => {
        clearInterval(scrollInterval);
        scrollChatToBottom();
    }, message.length * 15 + 500);
}

// Scroll chat to bottom
function scrollChatToBottom() {
    const chatContainer = $('#aiAskChatContainer');
    chatContainer.scrollTop(chatContainer[0].scrollHeight);
}
