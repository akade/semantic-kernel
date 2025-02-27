﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Http;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Reliability.Basic;
using Polly;
using RepoUtils;

// ReSharper disable once InconsistentNaming
public static class Example08_RetryHandler
{
    public static async Task RunAsync()
    {
        await DefaultNoRetryAsync();

        await ReliabilityBasicExtensionAsync();

        await ReliabilityPollyExtensionAsync();

        await CustomHandlerAsync();
    }

    private static async Task DefaultNoRetryAsync()
    {
        InfoLogger.Logger.LogInformation("============================== Kernel default behavior: No Retry ==============================");
        var kernel = InitializeKernelBuilder()
            .Build();

        await ImportAndExecutePluginAsync(kernel);
    }

    private static async Task ReliabilityBasicExtensionAsync()
    {
        InfoLogger.Logger.LogInformation("============================== Using Reliability.Basic extension ==============================");
        var retryConfig = new BasicRetryConfig
        {
            MaxRetryCount = 3,
            UseExponentialBackoff = true,
        };
        retryConfig.RetryableStatusCodes.Add(HttpStatusCode.Unauthorized);

        var kernel = InitializeKernelBuilder()
            .WithRetryBasic(retryConfig)
            .Build();

        await ImportAndExecutePluginAsync(kernel);
    }

    private static async Task ReliabilityPollyExtensionAsync()
    {
        InfoLogger.Logger.LogInformation("============================== Using Reliability.Polly extension ==============================");
        var kernel = InitializeKernelBuilder()
            .WithRetryPolly(GetPollyPolicy(InfoLogger.LoggerFactory))
            .Build();

        await ImportAndExecutePluginAsync(kernel);
    }

    private static async Task CustomHandlerAsync()
    {
        InfoLogger.Logger.LogInformation("============================== Using a Custom Http Handler ==============================");
        var kernel = InitializeKernelBuilder()
                        .WithHttpHandlerFactory(new MyCustomHandlerFactory())
                        .Build();

        await ImportAndExecutePluginAsync(kernel);
    }

    private static KernelBuilder InitializeKernelBuilder()
    {
        return new KernelBuilder()
                    .WithLoggerFactory(InfoLogger.LoggerFactory)
                    // OpenAI settings - you can set the OpenAI.ApiKey to an invalid value to see the retry policy in play
                    .WithOpenAIChatCompletionService(TestConfiguration.OpenAI.ChatModelId, "BAD_KEY");
    }

    private static AsyncPolicy<HttpResponseMessage> GetPollyPolicy(ILoggerFactory? logger)
    {
        // Handle 429 and 401 errors
        // Typically 401 would not be something we retry but for demonstration
        // purposes we are doing so as it's easy to trigger when using an invalid key.
        const int TooManyRequests = 429;
        const int Unauthorized = 401;

        return Policy
            .HandleResult<HttpResponseMessage>(response =>
                (int)response.StatusCode is TooManyRequests or Unauthorized)
            .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(8)
                },
                (outcome, timespan, retryCount, _)
                    => InfoLogger.Logger.LogWarning("Error executing action [attempt {RetryCount} of 3], pausing {PausingMilliseconds}ms. Outcome: {StatusCode}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        outcome.Result.StatusCode));
    }

    private static async Task ImportAndExecutePluginAsync(Kernel kernel)
    {
        // Load semantic plugin defined with prompt templates
        string folder = RepoFiles.SamplePluginsPath();

        kernel.ImportPluginFromObject<TimePlugin>();

        var qaPlugin = kernel.ImportPluginFromPromptDirectory(Path.Combine(folder, "QAPlugin"));

        var question = "How popular is Polly library?";

        InfoLogger.Logger.LogInformation("Question: {0}", question);
        // To see the retry policy in play, you can set the OpenAI.ApiKey to an invalid value
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            var answer = await kernel.RunAsync(question, qaPlugin["Question"]);
            InfoLogger.Logger.LogInformation("Answer: {0}", answer.GetValue<string>());
        }
        catch (Exception ex)
        {
            InfoLogger.Logger.LogInformation("Error: {0}", ex.Message);
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    // Basic custom retry handler factory
    public sealed class MyCustomHandlerFactory : HttpHandlerFactory<MyCustomHandler>
    {
    }

    // Basic custom empty retry handler
    public sealed class MyCustomHandler : DelegatingHandler
    {
        public MyCustomHandler(ILoggerFactory loggerFactory)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Your custom http handling implementation
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("My custom bad request override")
            });
        }
    }

    private static class InfoLogger
    {
        internal static ILogger Logger => LoggerFactory.CreateLogger("Example08_RetryHandler");
        internal static ILoggerFactory LoggerFactory => s_loggerFactory.Value;
        private static readonly Lazy<ILoggerFactory> s_loggerFactory = new(LogBuilder);

        private static ILoggerFactory LogBuilder()
        {
            return Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddFilter("Microsoft", LogLevel.Information);
                builder.AddFilter("Microsoft.SemanticKernel", LogLevel.Critical);
                builder.AddFilter("Microsoft.SemanticKernel.Reliability", LogLevel.Information);
                builder.AddFilter("System", LogLevel.Information);

                builder.AddConsole();
            });
        }
    }
}
