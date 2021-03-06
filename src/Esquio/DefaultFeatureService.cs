﻿using Esquio.Abstractions;
using Esquio.DependencyInjection;
using Esquio.Diagnostics;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Esquio
{
    internal sealed class DefaultFeatureService
        : IFeatureService
    {
        private readonly IRuntimeFeatureStore _featureStore;
        private readonly IToggleTypeActivator _toggleActivator;
        private readonly IScopedEvaluationHolder _session;
        private readonly EsquioOptions _options;
        private readonly EsquioDiagnostics _diagnostics;

        public DefaultFeatureService(
            IRuntimeFeatureStore store,
            IToggleTypeActivator toggleActivator,
            IScopedEvaluationHolder session,
            IOptions<EsquioOptions> options,
            EsquioDiagnostics diagnostics)
        {
            _featureStore = store ?? throw new ArgumentNullException(nameof(store));
            _toggleActivator = toggleActivator ?? throw new ArgumentNullException(nameof(toggleActivator));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }
        public async Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default)
        {
            var enabled = true;
            var correlationId = Guid.NewGuid();
            var deploymentName = _options.DefaultDeploymentName;
            var productName = _options.DefaultProductName;

            try
            {
                if (_options.ScopedEvaluationEnabled
                    &&
                    await _session.TryGetAsync(featureName, out var sessionResult))
                {
                    _diagnostics.FeatureEvaluationFromSession(featureName, productName, deploymentName);
                    enabled = sessionResult;
                }
                else
                {
                    // if evaluation session is not enabled ( true by default ) or this product/feature combination
                    // is not on the session store, evaluate it again!

                    enabled = await GetRuntimeEvaluationResult(featureName, productName, deploymentName, correlationId, cancellationToken);
                    if (_options.ScopedEvaluationEnabled)
                    {
                        // if session is enabled, set product/feature on the store to be reused
                        await _session.SetAsync(featureName, enabled);
                    }
                }

                return enabled;
            }
            catch (Exception exception)
            {
                _diagnostics.FeatureEvaluationThrow(correlationId, featureName, productName, deploymentName, exception);

                if (_options.OnErrorBehavior == OnErrorBehavior.Throw)
                {
                    throw;
                }

                return _options.OnErrorBehavior == OnErrorBehavior.SetEnabled;
            }
        }

        private async Task<bool> GetRuntimeEvaluationResult(string featureName, string productName, string deploymentName, Guid correlationId, CancellationToken cancellationToken = default)
        {
            var totalTime = ValueStopwatch.StartNew();
            var enabled = true;

            _diagnostics.BeginFeatureEvaluation(correlationId, featureName, productName, deploymentName);

            var feature = await _featureStore
                .FindFeatureAsync(featureName, productName, deploymentName, cancellationToken);

            if (feature == null)
            {
                _diagnostics.FeatureEvaluationNotFound(correlationId, featureName, productName, deploymentName);
                return _options.NotFoundBehavior == NotFoundBehavior.SetEnabled;
            }

            if (!feature.IsEnabled)
            {
                _diagnostics.FeatureEvaluationDisabled(featureName, productName, deploymentName);
                enabled = false;
            }
            else
            {
                var toggles = feature.GetToggles();

                foreach (var toggle in toggles)
                {
                    var toggleCorrelationId = Guid.NewGuid();

                    _diagnostics.BeginTogglevaluation(toggleCorrelationId, featureName, productName, deploymentName, toggle.Type);

                    var active = false;
                    var evaluationTime = ValueStopwatch.StartNew();

                    var toggleInstance = _toggleActivator
                        .CreateInstance(toggle.Type);

                    if (toggleInstance != null)
                    {
                        active = await toggleInstance.IsActiveAsync(
                            ToggleExecutionContext.FromToggle(featureName, productName, deploymentName, toggle),
                            cancellationToken);
                    }

                    _diagnostics.Togglevaluation(featureName, productName, deploymentName, toggle.Type, (long)evaluationTime.GetElapsedTime().TotalMilliseconds);
                    _diagnostics.EndTogglevaluation(toggleCorrelationId, featureName, productName, deploymentName, toggle.Type, active);

                    if (!active)
                    {
                        _diagnostics.ToggleNotActive(featureName, productName, deploymentName, toggle.Type);

                        enabled = false;
                        break;
                    }
                }
            }

            _diagnostics.EndFeatureEvaluation(correlationId, featureName, productName, deploymentName, (long)totalTime.GetElapsedTime().TotalMilliseconds, enabled);
            return enabled;
        }
    }
}
