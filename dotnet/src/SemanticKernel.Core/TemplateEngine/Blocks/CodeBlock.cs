﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Orchestration;

namespace Microsoft.SemanticKernel.TemplateEngine.Blocks;

#pragma warning disable CA2254 // error strings are used also internally, not just for logging
#pragma warning disable CA1031 // IsCriticalException is an internal utility and should not be used by extensions

// ReSharper disable TemplateIsNotCompileTimeConstantProblem
internal sealed class CodeBlock : Block, ICodeRendering
{
    internal override BlockTypes Type => BlockTypes.Code;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBlock"/> class.
    /// </summary>
    /// <param name="content">Block content</param>
    /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> to use for logging. If null, no logging will be performed.</param>
    public CodeBlock(string? content, ILoggerFactory? loggerFactory)
        : this(new CodeTokenizer(loggerFactory).Tokenize(content), content?.Trim(), loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBlock"/> class.
    /// </summary>
    /// <param name="tokens">A list of blocks</param>
    /// <param name="content">Block content</param>
    /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> to use for logging. If null, no logging will be performed.</param>
    public CodeBlock(List<Block> tokens, string? content, ILoggerFactory? loggerFactory)
        : base(content?.Trim(), loggerFactory)
    {
        this._tokens = tokens;
    }

    /// <inheritdoc/>
    public override bool IsValid(out string errorMsg)
    {
        errorMsg = "";

        foreach (Block token in this._tokens)
        {
            if (!token.IsValid(out errorMsg))
            {
                this.Logger.LogError(errorMsg);
                return false;
            }
        }

        if (this._tokens.Count > 0 && this._tokens[0].Type == BlockTypes.NamedArg)
        {
            errorMsg = "Unexpected named argument found. Expected function name first.";
            this.Logger.LogError(errorMsg);
            return false;
        }

        if (this._tokens.Count > 1 && !this.IsValidFunctionCall(out errorMsg))
        {
            return false;
        }

        this._validated = true;

        return true;
    }

    /// <inheritdoc/>
    public async Task<string> RenderCodeAsync(Kernel kernel, SKContext context, CancellationToken cancellationToken = default)
    {
        if (!this._validated && !this.IsValid(out var error))
        {
            throw new SKException(error);
        }

        this.Logger.LogTrace("Rendering code: `{Content}`", this.Content);

        return this._tokens[0].Type switch
        {
            BlockTypes.Value or BlockTypes.Variable => ((ITextRendering)this._tokens[0]).Render(context.Variables),
            BlockTypes.FunctionId => await this.RenderFunctionCallAsync((FunctionIdBlock)this._tokens[0], kernel, context).ConfigureAwait(false),
            _ => throw new SKException($"Unexpected first token type: {this._tokens[0].Type:G}"),
        };
    }

    #region private ================================================================================

    private bool _validated;
    private readonly List<Block> _tokens;

    private async Task<string> RenderFunctionCallAsync(FunctionIdBlock fBlock, Kernel kernel, SKContext context)
    {
        // Clone the context to avoid unexpected variable mutations from the inner function execution
        ContextVariables inputVariables = context.Variables.Clone();

        // If the code syntax is {{functionName $varName}} use $varName instead of $input
        // If the code syntax is {{functionName 'value'}} use "value" instead of $input
        if (this._tokens.Count > 1)
        {
            inputVariables = this.PopulateContextWithFunctionArguments(inputVariables);
        }
        try
        {
            await kernel.RunAsync(fBlock.PluginName, fBlock.FunctionName, inputVariables).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Function {Plugin}.{Function} execution failed with error {Error}", fBlock.PluginName, fBlock.FunctionName, ex.Message);
            throw;
        }

        return inputVariables.ToString();
    }

    private bool IsValidFunctionCall(out string errorMsg)
    {
        errorMsg = "";
        if (this._tokens[0].Type != BlockTypes.FunctionId)
        {
            errorMsg = $"Unexpected second token found: {this._tokens[1].Content}";
            this.Logger.LogError(errorMsg);
            return false;
        }

        if (this._tokens[1].Type is not BlockTypes.Value and not BlockTypes.Variable and not BlockTypes.NamedArg)
        {
            errorMsg = "The first arg of a function must be a quoted string, variable or named argument";
            this.Logger.LogError(errorMsg);
            return false;
        }

        for (int i = 2; i < this._tokens.Count; i++)
        {
            if (this._tokens[i].Type is not BlockTypes.NamedArg)
            {
                errorMsg = $"Functions only support named arguments after the first argument. Argument {i} is not named.";
                this.Logger.LogError(errorMsg);
                return false;
            }
        }

        return true;
    }

    private ContextVariables PopulateContextWithFunctionArguments(ContextVariables variables)
    {
        // Clone the context to avoid unexpected and hard to test input mutation
        var variablesClone = variables.Clone();
        var firstArg = this._tokens[1];

        // Sensitive data, logging as trace, disabled by default
        this.Logger.LogTrace("Passing variable/value: `{Content}`", firstArg.Content);

        var namedArgsStartIndex = 1;
        if (firstArg.Type is not BlockTypes.NamedArg)
        {
            string input = ((ITextRendering)this._tokens[1]).Render(variablesClone);
            // Keep previous trust information when updating the input
            variablesClone.Update(input);
            namedArgsStartIndex++;
        }

        for (int i = namedArgsStartIndex; i < this._tokens.Count; i++)
        {
            var arg = this._tokens[i] as NamedArgBlock;

            // When casting fails because the block isn't a NamedArg, arg is null
            if (arg == null)
            {
                var errorMsg = "Functions support up to one positional argument";
                this.Logger.LogError(errorMsg);
                throw new SKException($"Unexpected first token type: {this._tokens[i].Type:G}");
            }

            // Sensitive data, logging as trace, disabled by default
            this.Logger.LogTrace("Passing variable/value: `{Content}`", arg.Content);

            variablesClone.Set(arg.Name, arg.GetValue(variables));
        }

        return variablesClone;
    }
    #endregion
}
// ReSharper restore TemplateIsNotCompileTimeConstantProblem
#pragma warning restore CA2254
