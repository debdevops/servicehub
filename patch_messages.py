import re

with open('services/api/src/ServiceHub.Api/Controllers/V1/MessagesController.cs', 'r') as f:
    content = f.read()

# 1. Fields
content = re.sub(
    r'private readonly IMessageSender _messageSender;\s*private readonly IMessageReceiver _messageReceiver;',
    r'private readonly IEnumerable<ICloudMessagingProvider> _providers;',
    content
)

# 2. Constructor args
content = re.sub(
    r'IMessageSender messageSender,\s*IMessageReceiver messageReceiver,',
    r'IEnumerable<ICloudMessagingProvider> providers,',
    content
)

# 3. Constructor assignments
content = re.sub(
    r'_messageSender = messageSender[^;]+;\s*_messageReceiver = messageReceiver[^;]+;',
    r'_providers = providers ?? throw new ArgumentNullException(nameof(providers));',
    content
)

# 4. In PeekQueueMessages:
content = re.sub(
    r'var result = await _messageReceiver\.PeekMessagesAsync\(request, cancellationToken\);',
    r'var provider = _providers.FirstOrDefault(p => p.ProviderType == namespaceResult.Value.Provider) ?? throw new InvalidOperationException($"No provider registered for {namespaceResult.Value.Provider}");\n        var result = await provider.GetMessageReceiver().PeekMessagesAsync(request, cancellationToken);',
    content,
    count=1
)

# 5. In PeekSubscriptionMessages:
content = re.sub(
    r'var result = await _messageReceiver\.PeekMessagesAsync\(request, cancellationToken\);',
    r'var provider = _providers.FirstOrDefault(p => p.ProviderType == namespaceResult.Value.Provider) ?? throw new InvalidOperationException($"No provider registered for {namespaceResult.Value.Provider}");\n        var result = await provider.GetMessageReceiver().PeekMessagesAsync(request, cancellationToken);',
    content,
    count=1
)

# 6. In PeekQueueDeadLetterMessages:
content = re.sub(
    r'var result = await _messageReceiver\.PeekDeadLetterMessagesAsync\(request, cancellationToken\);',
    r'var provider = _providers.FirstOrDefault(p => p.ProviderType == namespaceResult.Value.Provider) ?? throw new InvalidOperationException($"No provider registered for {namespaceResult.Value.Provider}");\n        var result = await provider.GetMessageReceiver().PeekDeadLetterMessagesAsync(request, cancellationToken);',
    content,
    count=1
)

# 7. In PeekSubscriptionDeadLetterMessages:
content = re.sub(
    r'var result = await _messageReceiver\.PeekDeadLetterMessagesAsync\(request, cancellationToken\);',
    r'var provider = _providers.FirstOrDefault(p => p.ProviderType == namespaceResult.Value.Provider) ?? throw new InvalidOperationException($"No provider registered for {namespaceResult.Value.Provider}");\n        var result = await provider.GetMessageReceiver().PeekDeadLetterMessagesAsync(request, cancellationToken);',
    content,
    count=1
)

# 8. In ReplayMessage:
content = re.sub(
    r'var result = await _messageReceiver\.ReplayMessageAsync\(',
    r'var provider = _providers.FirstOrDefault(p => p.ProviderType == ns.Provider) ?? throw new InvalidOperationException($"No provider registered for {ns.Provider}");\n        var result = await provider.GetMessageReceiver().ReplayMessageAsync(',
    content,
    count=1
)

with open('services/api/src/ServiceHub.Api/Controllers/V1/MessagesController.cs', 'w') as f:
    f.write(content)

with open('services/api/tests/ServiceHub.UnitTests/Api/Controllers/V1/MessagesControllerTests.cs', 'r') as f:
    test_content = f.read()

# 1. Update constructor of MessagesControllerTests
new_constructor = """    public MessagesControllerTests()
    {
        _messageSender = new Mock<IMessageSender>();
        _messageReceiver = new Mock<IMessageReceiver>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<MessagesController>>();

        var mockProvider = new Mock<ICloudMessagingProvider>();
        mockProvider.Setup(p => p.ProviderType).Returns(CloudProviderType.Azure);
        mockProvider.Setup(p => p.GetMessageReceiver()).Returns(_messageReceiver.Object);
        mockProvider.Setup(p => p.GetMessageSender()).Returns(_messageSender.Object);

        _controller = new MessagesController(
            new[] { mockProvider.Object },
            _namespaceRepository.Object,
            _logger.Object)"""

test_content = re.sub(
    r'    public MessagesControllerTests\(\)\s*\{\s*_messageSender = new Mock<IMessageSender>\(\);\s*_messageReceiver = new Mock<IMessageReceiver>\(\);\s*_namespaceRepository = new Mock<INamespaceRepository>\(\);\s*_logger = new Mock<ILogger<MessagesController>>\(\);\s*_controller = new MessagesController\(\s*_messageSender\.Object,\s*_messageReceiver\.Object,\s*_namespaceRepository\.Object,\s*_logger\.Object\)',
    new_constructor,
    test_content
)

# 2. Fix Constructor_NullMessageSender_ShouldThrow and Constructor_NullMessageReceiver_ShouldThrow
# Instead of deleting them, we just replace them with Constructor_NullProviders_ShouldThrow
new_tests = """    [Fact]
    public void Constructor_NullProviders_ShouldThrow()
    {
        var act = () => new MessagesController(null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new MessagesController(Array.Empty<ICloudMessagingProvider>(), _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }"""

test_content = re.sub(
    r'    \[Fact\]\s*public void Constructor_NullMessageSender_ShouldThrow\(\)[\s\S]*?public void Constructor_NullLogger_ShouldThrow\(\)\s*\{\s*var act = \(\) => new MessagesController\([^)]+\);\s*act\.Should\(\)\.Throw<ArgumentNullException>\(\);\s*\}',
    new_tests,
    test_content
)

with open('services/api/tests/ServiceHub.UnitTests/Api/Controllers/V1/MessagesControllerTests.cs', 'w') as f:
    f.write(test_content)
