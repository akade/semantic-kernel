﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.TemplateEngine;
using Moq;
using Xunit;

// ReSharper disable StringLiteralTypo

namespace SemanticKernel.UnitTests.Functions;

public class SemanticFunctionTests
{
    [Fact]
    public void ItProvidesAccessToFunctionsViaFunctionCollection()
    {
        // Arrange
        var factory = new Mock<Func<ILoggerFactory, ITextCompletion>>();
        var kernel = new KernelBuilder()
            .WithDefaultAIService(factory.Object)
            .Build();

        kernel.Plugins.Add(new SKPlugin("jk", functions: new[] { kernel.CreateFunctionFromPrompt(promptTemplate: "Tell me a joke", functionName: "joker", description: "Nice fun") }));

        // Act & Assert - 3 functions, var name is not case sensitive
        Assert.True(kernel.Plugins.TryGetFunction("jk", "joker", out _));
        Assert.True(kernel.Plugins.TryGetFunction("JK", "JOKER", out _));
    }

    [Theory]
    [InlineData(null, "Assistant is a large language model.")]
    [InlineData("My Chat Prompt", "My Chat Prompt")]
    public async Task ItUsesChatSystemPromptWhenProvidedAsync(string providedSystemChatPrompt, string expectedSystemChatPrompt)
    {
        // Arrange
        var mockTextCompletion = new Mock<ITextCompletion>();
        var mockCompletionResult = new Mock<ITextResult>();

        mockTextCompletion.Setup(c => c.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mockCompletionResult.Object });
        mockCompletionResult.Setup(cr => cr.GetCompletionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("llmResult");

        var kernel = new KernelBuilder()
            .WithAIService("x", mockTextCompletion.Object)
            .Build();

        var templateConfig = new PromptTemplateConfig();
        templateConfig.ModelSettings.Add(new OpenAIRequestSettings()
        {
            ChatSystemPrompt = providedSystemChatPrompt
        });

        var func = kernel.CreateFunctionFromPrompt("template", templateConfig, "pluginName");

        // Act
        await kernel.RunAsync(func);

        // Assert
        mockTextCompletion.Verify(a => a.GetCompletionsAsync("template", It.Is<OpenAIRequestSettings>(c => c.ChatSystemPrompt == expectedSystemChatPrompt), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task ItUsesDefaultServiceWhenSpecifiedAsync()
    {
        // Arrange
        var mockTextCompletion1 = new Mock<ITextCompletion>();
        var mockTextCompletion2 = new Mock<ITextCompletion>();
        var mockCompletionResult = new Mock<ITextResult>();

        mockTextCompletion1.Setup(c => c.GetCompletionsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mockCompletionResult.Object });
        mockTextCompletion2.Setup(c => c.GetCompletionsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mockCompletionResult.Object });
        mockCompletionResult.Setup(cr => cr.GetCompletionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("llmResult");

        var kernel = new KernelBuilder()
            .WithAIService("service1", mockTextCompletion1.Object, false)
            .WithAIService("service2", mockTextCompletion2.Object, true)
            .Build();

        var templateConfig = new PromptTemplateConfig();
        var func = kernel.CreateFunctionFromPrompt("template", templateConfig, "pluginName");

        // Act
        await kernel.RunAsync(func);

        // Assert
        mockTextCompletion1.Verify(a => a.GetCompletionsAsync("template", null, It.IsAny<CancellationToken>()), Times.Never());
        mockTextCompletion2.Verify(a => a.GetCompletionsAsync("template", null, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task ItUsesServiceIdWhenProvidedAsync()
    {
        // Arrange
        var mockTextCompletion1 = new Mock<ITextCompletion>();
        var mockTextCompletion2 = new Mock<ITextCompletion>();
        var mockCompletionResult = new Mock<ITextResult>();

        mockTextCompletion1.Setup(c => c.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mockCompletionResult.Object });
        mockTextCompletion2.Setup(c => c.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mockCompletionResult.Object });
        mockCompletionResult.Setup(cr => cr.GetCompletionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("llmResult");

        var kernel = new KernelBuilder()
            .WithAIService("service1", mockTextCompletion1.Object, false)
            .WithAIService("service2", mockTextCompletion2.Object, true)
            .Build();

        var templateConfig = new PromptTemplateConfig();
        templateConfig.ModelSettings.Add(new AIRequestSettings() { ServiceId = "service1" });
        var func = kernel.CreateFunctionFromPrompt("template", templateConfig, "pluginName");

        // Act
        await kernel.RunAsync(func);

        // Assert
        mockTextCompletion1.Verify(a => a.GetCompletionsAsync("template", It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>()), Times.Once());
        mockTextCompletion2.Verify(a => a.GetCompletionsAsync("template", It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task ItFailsIfInvalidServiceIdIsProvidedAsync()
    {
        // Arrange
        var mockTextCompletion1 = new Mock<ITextCompletion>();
        var mockTextCompletion2 = new Mock<ITextCompletion>();

        var kernel = new KernelBuilder()
            .WithAIService("service1", mockTextCompletion1.Object, false)
            .WithAIService("service2", mockTextCompletion2.Object, true)
            .Build();

        var templateConfig = new PromptTemplateConfig();
        templateConfig.ModelSettings.Add(new AIRequestSettings() { ServiceId = "service3" });
        var func = kernel.CreateFunctionFromPrompt("template", templateConfig, "pluginName");

        // Act
        var exception = await Assert.ThrowsAsync<SKException>(() => kernel.RunAsync(func));

        // Assert
        Assert.Equal("Service of type Microsoft.SemanticKernel.AI.TextCompletion.ITextCompletion and name service3 not registered.", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RunAsyncHandlesPreInvocationAsync(int pipelineCount)
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction = SKFunctionFactory.CreateFromPrompt("Write a simple phrase about UnitTests");

        var invoked = 0;
        sut.FunctionInvoking += (sender, e) =>
        {
            invoked++;
        };
        List<ISKFunction> functions = new();
        for (int i = 0; i < pipelineCount; i++)
        {
            functions.Add(semanticFunction);
        }

        // Act
        var result = await sut.RunAsync(functions.ToArray());

        // Assert
        Assert.Equal(pipelineCount, invoked);
        mockTextCompletion.Verify(m => m.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>()), Times.Exactly(pipelineCount));
    }

    [Fact]
    public async Task RunAsyncHandlesPreInvocationWasCancelledAsync()
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction = SKFunctionFactory.CreateFromPrompt("Write a simple phrase about UnitTests");
        var input = "Test input";
        var invoked = false;
        sut.FunctionInvoking += (sender, e) =>
        {
            invoked = true;
            e.Cancel();
        };

        // Act
        var result = await sut.RunAsync(input, semanticFunction);

        // Assert
        Assert.True(invoked);
        Assert.Null(result);
    }

    [Fact]
    public async Task RunAsyncHandlesPreInvocationCancelationDontRunSubsequentFunctionsInThePipelineAsync()
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction = SKFunctionFactory.CreateFromPrompt("Write a simple phrase about UnitTests");

        var invoked = 0;
        sut.FunctionInvoking += (sender, e) =>
        {
            invoked++;
            e.Cancel();
        };

        // Act
        var result = await sut.RunAsync(semanticFunction, semanticFunction);

        // Assert
        Assert.Equal(1, invoked);
        mockTextCompletion.Verify(m => m.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsyncPreInvocationCancelationDontTriggerInvokedHandlerAsync()
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction = SKFunctionFactory.CreateFromPrompt("Write a simple phrase about UnitTests");
        var invoked = 0;

        sut.FunctionInvoking += (sender, e) =>
        {
            e.Cancel();
        };

        sut.FunctionInvoked += (sender, e) =>
        {
            invoked++;
        };

        // Act
        var result = await sut.RunAsync(semanticFunction);

        // Assert
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task RunAsyncPreInvocationSkipDontTriggerInvokedHandlerAsync()
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction1 = SKFunctionFactory.CreateFromPrompt("Write one phrase about UnitTests", functionName: "SkipMe");
        var semanticFunction2 = SKFunctionFactory.CreateFromPrompt("Write two phrases about UnitTests", functionName: "DontSkipMe");
        var invoked = 0;
        var invoking = 0;
        string invokedFunction = string.Empty;

        sut.FunctionInvoking += (sender, e) =>
        {
            invoking++;
            if (e.FunctionView.Name == "SkipMe")
            {
                e.Skip();
            }
        };

        sut.FunctionInvoked += (sender, e) =>
        {
            invokedFunction = e.FunctionView.Name;
            invoked++;
        };

        // Act
        var result = await sut.RunAsync(
            semanticFunction1,
            semanticFunction2);

        // Assert
        Assert.Equal(2, invoking);
        Assert.Equal(1, invoked);
        Assert.Equal("DontSkipMe", invokedFunction);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RunAsyncHandlesPostInvocationAsync(int pipelineCount)
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var semanticFunction = SKFunctionFactory.CreateFromPrompt("Write a simple phrase about UnitTests");

        var invoked = 0;

        sut.FunctionInvoked += (sender, e) =>
        {
            invoked++;
        };

        List<ISKFunction> functions = new();
        for (int i = 0; i < pipelineCount; i++)
        {
            functions.Add(semanticFunction);
        }

        // Act
        var result = await sut.RunAsync(functions.ToArray());

        // Assert
        Assert.Equal(pipelineCount, invoked);
        mockTextCompletion.Verify(m => m.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>()), Times.Exactly(pipelineCount));
    }

    [Fact]
    public async Task RunAsyncChangeVariableInvokingHandlerAsync()
    {
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var prompt = "Write a simple phrase about UnitTests {{$input}}";
        var semanticFunction = SKFunctionFactory.CreateFromPrompt(prompt);

        var originalInput = "Importance";
        var newInput = "Problems";

        sut.FunctionInvoking += (sender, e) =>
        {
            originalInput = newInput;
        };

        // Act
        await sut.RunAsync(originalInput, semanticFunction);

        // Assert
        Assert.Equal(newInput, originalInput);
    }

    [Fact]
    public async Task RunAsyncChangeVariableInvokedHandlerAsync()
    {
        var (mockTextResult, mockTextCompletion) = this.SetupMocks();
        var sut = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();
        var prompt = "Write a simple phrase about UnitTests {{$input}}";
        var semanticFunction = SKFunctionFactory.CreateFromPrompt(prompt);

        var originalInput = "Importance";
        var newInput = "Problems";

        sut.FunctionInvoked += (sender, e) =>
        {
            originalInput = newInput;
        };

        // Act
        await sut.RunAsync(originalInput, semanticFunction);

        // Assert
        Assert.Equal(newInput, originalInput);
    }

    [Fact]
    public async Task ItReturnsFunctionResultsCorrectlyAsync()
    {
        // Arrange
        var (mockTextResult, mockTextCompletion) = this.SetupMocks("Result3");
        var kernel = new KernelBuilder().WithAIService<ITextCompletion>(null, mockTextCompletion.Object).Build();

        var function1 = kernel.CreateFunctionFromMethod(() => "Result1", "Function1");
        var function2 = kernel.CreateFunctionFromMethod(() => "Result2", "Function2");
        var function3 = kernel.CreateFunctionFromPrompt("Write a simple phrase about UnitTests", functionName: "Function3");

        // Act
        var result = await kernel.RunAsync(function1, function2, function3);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Result3", result.GetValue<string>());
    }

    private (Mock<ITextResult> textResultMock, Mock<ITextCompletion> textCompletionMock) SetupMocks(string? completionResult = null)
    {
        var mockTextResult = new Mock<ITextResult>();
        mockTextResult.Setup(m => m.GetCompletionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(completionResult ?? "LLM Result about UnitTests");

        var mockTextCompletion = new Mock<ITextCompletion>();
        mockTextCompletion.Setup(m => m.GetCompletionsAsync(It.IsAny<string>(), It.IsAny<AIRequestSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ITextResult> { mockTextResult.Object });

        return (mockTextResult, mockTextCompletion);
    }

    private static MethodInfo Method(Delegate method)
    {
        return method.Method;
    }
}
