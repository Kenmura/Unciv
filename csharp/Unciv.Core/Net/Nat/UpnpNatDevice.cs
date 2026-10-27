using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Unciv.Net.Nat;

/// <summary>
/// UPnP IGD (Internet Gateway Device) port mapping client using SSDP and SOAP (SERVER.md 6.5).
/// BCL-only, lightweight, handles WANIPConnection and WANPPPConnection services.
/// </summary>
public sealed class UpnpNatDevice : INatDevice, IDisposable
{
    private static readonly Regex ExternalIpRegex = new(@"<NewExternalIPAddress>(?<ip>[^<]+)</NewExternalIPAddress>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Uri controlUri;
    private readonly string serviceType;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public UpnpNatDevice(Uri controlUri, string serviceType, HttpClient? httpClient = null)
    {
        this.controlUri = controlUri;
        this.serviceType = serviceType;
        if (httpClient != null)
        {
            this.httpClient = httpClient;
            ownsHttpClient = false;
        }
        else
        {
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            ownsHttpClient = true;
        }
    }

    public NatProtocol Protocol => NatProtocol.Upnp;

    public Uri ControlUri => controlUri;

    public string ServiceType => serviceType;

    public async Task<IPAddress?> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        var body = $"""
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:GetExternalIPAddress xmlns:u="{serviceType}">
                </u:GetExternalIPAddress>
              </s:Body>
            </s:Envelope>
            """;

        var response = await SendSoapAsync("GetExternalIPAddress", body, cancellationToken).ConfigureAwait(false);
        if (response == null) return null;

        var match = ExternalIpRegex.Match(response);
        if (match.Success && IPAddress.TryParse(match.Groups["ip"].Value, out var ip))
        {
            return ip;
        }

        return null;
    }

    public async Task<bool> AddPortMappingAsync(
        int internalPort,
        int externalPort,
        IPAddress localAddress,
        string protocol = "TCP",
        string description = "Unciv",
        int leaseDurationSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        var proto = protocol.ToUpperInvariant();
        var body = string.Format(
            CultureInfo.InvariantCulture,
            """
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:AddPortMapping xmlns:u="{0}">
                  <NewRemoteHost></NewRemoteHost>
                  <NewExternalPort>{1}</NewExternalPort>
                  <NewProtocol>{2}</NewProtocol>
                  <NewInternalPort>{3}</NewInternalPort>
                  <NewInternalClient>{4}</NewInternalClient>
                  <NewEnabled>1</NewEnabled>
                  <NewPortMappingDescription>{5}</NewPortMappingDescription>
                  <NewLeaseDuration>{6}</NewLeaseDuration>
                </u:AddPortMapping>
              </s:Body>
            </s:Envelope>
            """,
            serviceType,
            externalPort,
            proto,
            internalPort,
            localAddress,
            description,
            leaseDurationSeconds);

        var response = await SendSoapAsync("AddPortMapping", body, cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    public async Task<bool> DeletePortMappingAsync(
        int externalPort,
        string protocol = "TCP",
        CancellationToken cancellationToken = default)
    {
        var proto = protocol.ToUpperInvariant();
        var body = string.Format(
            CultureInfo.InvariantCulture,
            """
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:DeletePortMapping xmlns:u="{0}">
                  <NewRemoteHost></NewRemoteHost>
                  <NewExternalPort>{1}</NewExternalPort>
                  <NewProtocol>{2}</NewProtocol>
                </u:DeletePortMapping>
              </s:Body>
            </s:Envelope>
            """,
            serviceType,
            externalPort,
            proto);

        var response = await SendSoapAsync("DeletePortMapping", body, cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    private async Task<string?> SendSoapAsync(string action, string soapBody, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, controlUri);
            request.Headers.Add("SOAPACTION", $"\"{serviceType}#{action}\"");
            request.Content = new StringContent(soapBody, Encoding.UTF8, "text/xml");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
