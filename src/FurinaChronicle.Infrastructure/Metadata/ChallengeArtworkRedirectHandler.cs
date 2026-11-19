// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Infrastructure.Metadata;

/// <summary>Follows only bounded, credential-free public artwork redirects; the transport must disable automatic redirects.</summary>
public sealed class ChallengeArtworkRedirectHandler(HttpMessageHandler transport) : DelegatingHandler(transport)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri? source = request.RequestUri;
        for (int hop = 0; hop <= 3; hop++)
        {
            if (!ChallengeArtworkUri.IsSupported(source))
                throw new HttpRequestException("Unsupported public artwork address.");
            using var next = new HttpRequestMessage(HttpMethod.Get, source);
            next.Headers.TryAddWithoutValidation("User-Agent", "FurinaChronicle/1.0");
            HttpResponseMessage response = await base.SendAsync(next, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            Uri? target = response.Headers.Location;
            response.Dispose();
            if (hop == 3 || target is null)
                throw new HttpRequestException("The public artwork redirect limit was reached.");
            source = target.IsAbsoluteUri ? target : new Uri(source!, target);
        }
        throw new HttpRequestException("The public artwork redirect limit was reached.");
    }
}
