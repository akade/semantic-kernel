﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HandlebarsDotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.Orchestration;

namespace Microsoft.SemanticKernel.TemplateEngine.Handlebars;

internal class HandlebarsPromptTemplate : IPromptTemplate
{
    /// <summary>
    /// Constructor for PromptTemplate.
    /// </summary>
    /// <param name="templateString">Prompt template string.</param>
    /// <param name="promptTemplateConfig">Prompt template configuration</param>
    /// <param name="loggerFactory">Logger factory</param>
    public HandlebarsPromptTemplate(string templateString, PromptTemplateConfig promptTemplateConfig, ILoggerFactory? loggerFactory = null)
    {
        this._loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        this._logger = this._loggerFactory.CreateLogger(typeof(HandlebarsPromptTemplate));
        this._templateString = templateString;
        this._promptTemplateConfig = promptTemplateConfig;
        this._parameters = new(() => this.InitParameters());
    }

    /// <inheritdoc/>
    public IReadOnlyList<SKParameterMetadata> Parameters => this._parameters.Value;

    /// <inheritdoc/>
    public async Task<string> RenderAsync(Kernel kernel, SKContext executionContext, CancellationToken cancellationToken = default)
    {
        var handlebars = HandlebarsDotNet.Handlebars.Create();

        foreach (ISKPlugin plugin in kernel.Plugins)
        {
            foreach (ISKFunction skfunction in plugin)
            {
                handlebars.RegisterHelper($"{plugin.Name}_{skfunction.Name}", (writer, hcontext, parameters) =>
                {
                    var result = skfunction.InvokeAsync(kernel, executionContext).GetAwaiter().GetResult();
                    writer.WriteSafeString(result.GetValue<string>());
                });
            }
        }

        var template = handlebars.Compile(this._templateString);

        var prompt = template(this.GetVariables(executionContext));

        return await Task.FromResult(prompt).ConfigureAwait(true);
    }

    #region private
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly string _templateString;
    private readonly PromptTemplateConfig _promptTemplateConfig;
    private readonly Lazy<IReadOnlyList<SKParameterMetadata>> _parameters;

    private List<SKParameterMetadata> InitParameters()
    {
        List<SKParameterMetadata> parameters = new(this._promptTemplateConfig.Input.Parameters.Count);
        foreach (var p in this._promptTemplateConfig.Input.Parameters)
        {
            parameters.Add(new SKParameterMetadata(p.Name)
            {
                Description = p.Description,
                DefaultValue = p.DefaultValue
            });
        }

        return parameters;
    }

    private Dictionary<string, string> GetVariables(SKContext executionContext)
    {
        Dictionary<string, string> variables = new();
        foreach (var p in this._promptTemplateConfig.Input.Parameters)
        {
            if (!string.IsNullOrEmpty(p.DefaultValue))
            {
                variables[p.Name] = p.DefaultValue;
            }
        }

        foreach (var kvp in executionContext.Variables)
        {
            variables.Add(kvp.Key, kvp.Value);
        }

        return variables;
    }

    #endregion

}
