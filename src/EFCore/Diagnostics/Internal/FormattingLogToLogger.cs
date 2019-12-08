// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.Extensions.Logging;

namespace Microsoft.EntityFrameworkCore.Diagnostics.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class FormattingLogToLogger : ILogToLogger
    {
        [NotNull]
        private readonly Action<string> _sink;

        [NotNull]
        private readonly Func<EventId, LogLevel, bool> _filter;

        private readonly LogToOptions _options;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public FormattingLogToLogger(
            [NotNull] Action<string> sink,
            [NotNull] Func<EventId, LogLevel, bool> filter,
            LogToOptions options)
        {
            _sink = sink;
            _filter = filter;
            _options = options;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void Log(EventData eventData)
        {
            Check.NotNull(eventData, nameof(eventData));

            var message = eventData.ToString();
            var logLevel = eventData.LogLevel;
            var eventId = eventData.EventId;

            if (_options != LogToOptions.None)
            {
                var messageBuilder = new StringBuilder();

                if ((_options & LogToOptions.Level) != 0)
                {
                    messageBuilder.Append(GetLogLevelString(logLevel));
                }

                if ((_options & LogToOptions.LocalTime) != 0)
                {
                    messageBuilder.Append(DateTime.Now.ToShortDateString()).Append(DateTime.Now.ToString(" HH:mm:ss.fff "));
                }

                if ((_options & LogToOptions.UtcTime) != 0)
                {
                    messageBuilder.Append(DateTime.UtcNow.ToString("o")).Append(' ');
                }

                if ((_options & LogToOptions.Id) != 0)
                {
                    messageBuilder.Append(eventData.EventIdCode).Append('[').Append(eventId.Id).Append("] ");
                }

                if ((_options & LogToOptions.Category) != 0)
                {
                    var lastDot = eventId.Name.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        messageBuilder.Append('(').Append(eventId.Name.Substring(0, lastDot)).Append(") ");
                    }
                }

                const string padding = "      ";
                var preambleLength = messageBuilder.Length;

                if (_options == LogToOptions.SingleLine) // Single line ONLY
                {
                    message = messageBuilder
                        .Append(message)
                        .Replace(Environment.NewLine, "")
                        .ToString();
                }
                else
                {
                    message = (_options & LogToOptions.SingleLine) != 0
                        ? messageBuilder
                            .Append("-> ")
                            .Append(message)
                            .Replace(Environment.NewLine, "", preambleLength, messageBuilder.Length - preambleLength)
                            .ToString()
                        : messageBuilder
                            .AppendLine()
                            .Append(message)
                            .Replace(Environment.NewLine, Environment.NewLine + padding, preambleLength, messageBuilder.Length - preambleLength)
                            .ToString();
                }
            }

            _sink(message);
        }

        /// <inheritdoc />
        public virtual bool ShouldLog(EventId eventId, LogLevel logLevel)
            => _filter(eventId, logLevel);

        private static string GetLogLevelString(LogLevel logLevel)
            => logLevel switch
            {
                LogLevel.Trace => "trce: ",
                LogLevel.Debug => "dbug: ",
                LogLevel.Information => "info: ",
                LogLevel.Warning => "warn: ",
                LogLevel.Error => "fail: ",
                LogLevel.Critical => "crit: ",
                _ => "none",
            };
    }
}
