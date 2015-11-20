using Microsoft.IdentityModel;
using Microsoft.IdentityModel.S2S.Protocols.OAuth2;
using Microsoft.IdentityModel.S2S.Tokens;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.EventReceivers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IdentityModel.Selectors;
using System.IdentityModel.Tokens;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceModel;
using System.Text;
using System.Web;
using System.Web.Configuration;
using System.Web.Script.Serialization;
using AudienceRestriction = Microsoft.IdentityModel.Tokens.AudienceRestriction;
using AudienceUriValidationFailedException = Microsoft.IdentityModel.Tokens.AudienceUriValidationFailedException;
using SecurityTokenHandlerConfiguration = Microsoft.IdentityModel.Tokens.SecurityTokenHandlerConfiguration;
using X509SigningCredentials = Microsoft.IdentityModel.SecurityTokenService.X509SigningCredentials;

namespace <%= solutionName %>Web
{
    public static class TokenHelper
    {
        #region Öffentliche Felder

        /// <summary>
        /// SharePoint-Prinzipal.
        /// </summary>
        public const string SharePointPrincipal = "00000003-0000-0ff1-ce00-000000000000";

        /// <summary>
        /// Lebensdauer des HighTrust-Zugriffstokens, 12 Stunden.
        /// </summary>
        public static readonly TimeSpan HighTrustAccessTokenLifetime = TimeSpan.FromHours(12.0);

        #endregion public fields

        #region öffentliche Methoden

        /// <summary>
        /// Ruft die Kontexttokenzeichenfolge aus der angegebenen Anforderung ab durch Suchen nach bekannten Parameternamen in den
        /// Formular-Parametern (mit POST) und querystring. Gibt NULL zurück, wenn kein Kontexttoken gefunden wird.
        /// </summary>
        /// <param name="request">HttpRequest, in der nach einem Kontexttoken gesucht werden soll.</param>
        /// <returns>Die Kontexttokenzeichenfolge</returns>
        public static string GetContextTokenFromRequest(HttpRequest request)
        {
            return GetContextTokenFromRequest(new HttpRequestWrapper(request));
        }

        /// <summary>
        /// Ruft die Kontexttokenzeichenfolge aus der angegebenen Anforderung ab durch Suchen nach bekannten Parameternamen in den
        /// Formular-Parametern (mit POST) und querystring. Gibt NULL zurück, wenn kein Kontexttoken gefunden wird.
        /// </summary>
        /// <param name="request">HttpRequest, in der nach einem Kontexttoken gesucht werden soll.</param>
        /// <returns>Die Kontexttokenzeichenfolge</returns>
        public static string GetContextTokenFromRequest(HttpRequestBase request)
        {
            string[] paramNames = { "AppContext", "AppContextToken", "AccessToken", "SPAppToken" };
            foreach (string paramName in paramNames)
            {
                if (!string.IsNullOrEmpty(request.Form[paramName]))
                {
                    return request.Form[paramName];
                }
                if (!string.IsNullOrEmpty(request.QueryString[paramName]))
                {
                    return request.QueryString[paramName];
                }
            }
            return null;
        }

        /// <summary>
        /// Überprüfen Sie, ob eine angegebene Kontexttokenzeichenfolge für diese Anwendung vorgesehen ist, basierend auf den Parametern,
        /// die in web.config. angegeben sind. Zu den aus web.config verwendeten Parametern für die Validierung gehören ClientId,
        /// HostedAppHostNameOverride, HostedAppHostName, ClientSecret und Realm ist (falls angegeben). Wenn HostedAppHostNameOverride vorhanden ist,
        /// wird für die Validierung verwendet. Ansonsten, wenn der <paramref name="appHostName"/> nicht
        /// NULL, wird er stattdessen für die Validierung von HostedAppHostName der web.config verwendet. Wenn das Token ungültig ist, wird eine
        /// wird eine Ausnahme ausgelöst. Wenn das Token gültig ist, wird die STS-Metadaten-URL von TokenHelper auf Grundlage der Tokeninhalte aktualisiert.
        /// und es wird ein auf dem Kontexttoken basierendes JsonWebSecurityToken zurückgegeben.
        /// </summary>
        /// <param name="contextTokenString">Das zu überprüfende Kontexttoken</param>
        /// <param name="appHostName">Die URL-Autorität, die aus dem DNS-Hostnamen (Domain Name System) oder der IP-Adresse und der Portnummer besteht und zur Validierung von Tokenzielgruppen verwendet wird.
        /// Bei NULL wird die web.config-Einstellung HostedAppHostName stattdessen verwendet. Die web.config-Einstellung HostedAppHostNameOverride wird, falls vorhanden, verwendet
        /// für die Validierung anstelle von <paramref name="appHostName"/> .</param>
        /// <returns>Ein auf dem Kontexttoken basierendes JsonWebSecurityToken</returns>
        public static SharePointContextToken ReadAndValidateContextToken(string contextTokenString, string appHostName = null)
        {
            JsonWebSecurityTokenHandler tokenHandler = CreateJsonWebSecurityTokenHandler();
            SecurityToken securityToken = tokenHandler.ReadToken(contextTokenString);
            JsonWebSecurityToken jsonToken = securityToken as JsonWebSecurityToken;
            SharePointContextToken token = SharePointContextToken.Create(jsonToken);

            string stsAuthority = (new Uri(token.SecurityTokenServiceUri)).Authority;
            int firstDot = stsAuthority.IndexOf('.');

            GlobalEndPointPrefix = stsAuthority.Substring(0, firstDot);
            AcsHostUrl = stsAuthority.Substring(firstDot + 1);

            tokenHandler.ValidateToken(jsonToken);

            string[] acceptableAudiences;
            if (!String.IsNullOrEmpty(HostedAppHostNameOverride))
            {
                acceptableAudiences = HostedAppHostNameOverride.Split(';');
            }
            else if (appHostName == null)
            {
                acceptableAudiences = new[] { HostedAppHostName };
            }
            else
            {
                acceptableAudiences = new[] { appHostName };
            }

            bool validationSuccessful = false;
            string realm = Realm ?? token.Realm;
            foreach (var audience in acceptableAudiences)
            {
                string principal = GetFormattedPrincipal(ClientId, audience, realm);
                if (StringComparer.OrdinalIgnoreCase.Equals(token.Audience, principal))
                {
                    validationSuccessful = true;
                    break;
                }
            }

            if (!validationSuccessful)
            {
                throw new AudienceUriValidationFailedException(
                    String.Format(CultureInfo.CurrentCulture,
                    "\"{0}\" is not the intended audience \"{1}\"", String.Join(";", acceptableAudiences), token.Audience));
            }

            return token;
        }

        /// <summary>
        /// Ruft ein Zugriffstoken vom ACS ab, um die Quelle des angegebenen Kontexttokens im angegebenen
        /// targetHost. Der targetHost muss für den Prinzipal registriert sein, der das Kontexttoken gesendet hat.
        /// </summary>
        /// <param name="contextToken">Kontexttoken wird von der beabsichtigten Zielgruppe für das Zugriffstoken ausgestellt.</param>
        /// <param name="targetHost">URL-Autorität des Zielprinzipals</param>
        /// <returns>Ein Zugriffstoken mit einer Zielgruppe, das mit der Quelle des Kontexttokens übereinstimmt.</returns>
        public static OAuth2AccessTokenResponse GetAccessToken(SharePointContextToken contextToken, string targetHost)
        {
            string targetPrincipalName = contextToken.TargetPrincipalName;

            // Extrahieren Sie refreshToken aus dem Kontexttoken.
            string refreshToken = contextToken.RefreshToken;

            if (String.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

            string targetRealm = Realm ?? contextToken.Realm;

            return GetAccessToken(refreshToken,
                                  targetPrincipalName,
                                  targetHost,
                                  targetRealm);
        }

        /// <summary>
        /// Verwendet den angegebenen Autorisierungscode, um ein Zugriffstoken vom ACS für den Aufruf des angegebenen Prinzipals
        /// am angegebenen targetHost abzurufen. Der targetHost muss für den Zielprinzipal registriert sein.  Wenn der angegebene Bereich
        /// NULL ist, wird die Einstellung "Realm" in web.config stattdessen verwendet.
        /// </summary>
        /// <param name="authorizationCode">Autorisierungscode zum Austausch für Zugriffstoken</param>
        /// <param name="targetPrincipalName">Name des Zielprinzipals zum Abrufen eines Zugriffstokens für</param>
        /// <param name="targetHost">URL-Autorität des Zielprinzipals</param>
        /// <param name="targetRealm">Zu verwendender Bereich für Namens-ID und Zielgruppe des Zugriffstokens</param>
        /// <param name="redirectUri">Für diese App registrierten URI umleiten</param>
        /// <returns>Ein Zugriffstoken mit einer Zielgruppe des Zielprinzipals</returns>
        public static OAuth2AccessTokenResponse GetAccessToken(
            string authorizationCode,
            string targetPrincipalName,
            string targetHost,
            string targetRealm,
            Uri redirectUri)
        {
            if (targetRealm == null)
            {
                targetRealm = Realm;
            }

            string resource = GetFormattedPrincipal(targetPrincipalName, targetHost, targetRealm);
            string clientId = GetFormattedPrincipal(ClientId, null, targetRealm);

            // Erstellen Sie eine Anforderung für ein Token. RedirectUri ist hier NULL.  Dieser Vorgang schlägt fehl, wenn ein Umleitungs-URI registriert ist.
            OAuth2AccessTokenRequest oauth2Request =
                OAuth2MessageFactory.CreateAccessTokenRequestWithAuthorizationCode(
                    clientId,
                    ClientSecret,
                    authorizationCode,
                    redirectUri,
                    resource);

            // Token abrufen
            OAuth2S2SClient client = new OAuth2S2SClient();
            OAuth2AccessTokenResponse oauth2Response;
            try
            {
                oauth2Response =
                    client.Issue(AcsMetadataParser.GetStsUrl(targetRealm), oauth2Request) as OAuth2AccessTokenResponse;
            }
            catch (WebException wex)
            {
                using (StreamReader sr = new StreamReader(wex.Response.GetResponseStream()))
                {
                    string responseText = sr.ReadToEnd();
                    throw new WebException(wex.Message + " - " + responseText, wex);
                }
            }

            return oauth2Response;
        }

        /// <summary>
        /// Verwendet das angegebene refresh-Token, um ein Zugriffstoken vom ACS für den Aufruf des angegebenen Prinzipals
        /// am angegebenen targetHost abzurufen. Der targetHost muss für den Zielprinzipal registriert sein.  Wenn der angegebene Bereich
        /// NULL ist, wird die Einstellung "Realm" in web.config stattdessen verwendet.
        /// </summary>
        /// <param name="refreshToken">Refresh-Token zum Austausch für Zugriffstoken</param>
        /// <param name="targetPrincipalName">Name des Zielprinzipals zum Abrufen eines Zugriffstokens für</param>
        /// <param name="targetHost">URL-Autorität des Zielprinzipals</param>
        /// <param name="targetRealm">Zu verwendender Bereich für Namens-ID und Zielgruppe des Zugriffstokens</param>
        /// <returns>Ein Zugriffstoken mit einer Zielgruppe des Zielprinzipals</returns>
        public static OAuth2AccessTokenResponse GetAccessToken(
            string refreshToken,
            string targetPrincipalName,
            string targetHost,
            string targetRealm)
        {
            if (targetRealm == null)
            {
                targetRealm = Realm;
            }

            string resource = GetFormattedPrincipal(targetPrincipalName, targetHost, targetRealm);
            string clientId = GetFormattedPrincipal(ClientId, null, targetRealm);

            OAuth2AccessTokenRequest oauth2Request = OAuth2MessageFactory.CreateAccessTokenRequestWithRefreshToken(clientId, ClientSecret, refreshToken, resource);

            // Token abrufen
            OAuth2S2SClient client = new OAuth2S2SClient();
            OAuth2AccessTokenResponse oauth2Response;
            try
            {
                oauth2Response =
                    client.Issue(AcsMetadataParser.GetStsUrl(targetRealm), oauth2Request) as OAuth2AccessTokenResponse;
            }
            catch (WebException wex)
            {
                using (StreamReader sr = new StreamReader(wex.Response.GetResponseStream()))
                {
                    string responseText = sr.ReadToEnd();
                    throw new WebException(wex.Message + " - " + responseText, wex);
                }
            }

            return oauth2Response;
        }

        /// <summary>
        /// Ruft ein Zugriffstoken nur für Anwendungen vom ACS ab zum Aufrufen des angegebenen Prinzipals
        /// am angegebenen targetHost abzurufen. Der targetHost muss für den Zielprinzipal registriert sein.  Wenn der angegebene Bereich
        /// NULL ist, wird die Einstellung "Realm" in web.config stattdessen verwendet.
        /// </summary>
        /// <param name="targetPrincipalName">Name des Zielprinzipals zum Abrufen eines Zugriffstokens für</param>
        /// <param name="targetHost">URL-Autorität des Zielprinzipals</param>
        /// <param name="targetRealm">Zu verwendender Bereich für Namens-ID und Zielgruppe des Zugriffstokens</param>
        /// <returns>Ein Zugriffstoken mit einer Zielgruppe des Zielprinzipals</returns>
        public static OAuth2AccessTokenResponse GetAppOnlyAccessToken(
            string targetPrincipalName,
            string targetHost,
            string targetRealm)
        {

            if (targetRealm == null)
            {
                targetRealm = Realm;
            }

            string resource = GetFormattedPrincipal(targetPrincipalName, targetHost, targetRealm);
            string clientId = GetFormattedPrincipal(ClientId, HostedAppHostName, targetRealm);

            OAuth2AccessTokenRequest oauth2Request = OAuth2MessageFactory.CreateAccessTokenRequestWithClientCredentials(clientId, ClientSecret, resource);
            oauth2Request.Resource = resource;

            // Token abrufen
            OAuth2S2SClient client = new OAuth2S2SClient();

            OAuth2AccessTokenResponse oauth2Response;
            try
            {
                oauth2Response =
                    client.Issue(AcsMetadataParser.GetStsUrl(targetRealm), oauth2Request) as OAuth2AccessTokenResponse;
            }
            catch (WebException wex)
            {
                using (StreamReader sr = new StreamReader(wex.Response.GetResponseStream()))
                {
                    string responseText = sr.ReadToEnd();
                    throw new WebException(wex.Message + " - " + responseText, wex);
                }
            }

            return oauth2Response;
        }

        /// <summary>
        /// Erstellt einen Clientkontext auf der Grundlage der Eigenschaften eines Remoteereignisempfängers.
        /// </summary>
        /// <param name="properties">Eigenschaften eines Remoteereignisempfängers</param>
        /// <returns>Ein ClientContext bereit zum Webaufruf, aus dem das Ereignis stammt.</returns>
        public static ClientContext CreateRemoteEventReceiverClientContext(SPRemoteEventProperties properties)
        {
            Uri sharepointUrl;
            if (properties.ListEventProperties != null)
            {
                sharepointUrl = new Uri(properties.ListEventProperties.WebUrl);
            }
            else if (properties.ItemEventProperties != null)
            {
                sharepointUrl = new Uri(properties.ItemEventProperties.WebUrl);
            }
            else if (properties.WebEventProperties != null)
            {
                sharepointUrl = new Uri(properties.WebEventProperties.FullUrl);
            }
            else
            {
                return null;
            }

            if (IsHighTrustApp())
            {
                return GetS2SClientContextWithWindowsIdentity(sharepointUrl, null);
            }

            return CreateAcsClientContextForUrl(properties, sharepointUrl);
        }

        /// <summary>
        /// Erstellt einen Clientkontext auf der Grundlage der Eigenschaften eines App-Ereignisses.
        /// </summary>
        /// <param name="properties">Eigenschaften eines App-Ereignisses</param>
        /// <param name="useAppWeb">"True" für die Ausrichtung auf das App-Web, "False" für die Ausrichtung auf das Hostweb</param>
        /// <returns>Ein ClientContext, der zum Aufrufen der App-Web oder des übergeordneten Webs bereit ist.</returns>
        public static ClientContext CreateAppEventClientContext(SPRemoteEventProperties properties, bool useAppWeb)
        {
            if (properties.AppEventProperties == null)
            {
                return null;
            }

            Uri sharepointUrl = useAppWeb ? properties.AppEventProperties.AppWebFullUrl : properties.AppEventProperties.HostWebFullUrl;
            if (IsHighTrustApp())
            {
                return GetS2SClientContextWithWindowsIdentity(sharepointUrl, null);
            }

            return CreateAcsClientContextForUrl(properties, sharepointUrl);
        }

        /// <summary>
        /// Ruft ein Zugriffstoken vom ACS mithilfe des angegebenen Autorisierungscodes ab und verwendet das Zugriffstoken zum
        /// Erstellen eines Clientkontexts.
        /// </summary>
        /// <param name="targetUrl">URL der Ziel-SharePoint-Website</param>
        /// <param name="authorizationCode">Autorisierungscode für das Abrufen des Zugriffstokens vom ACS</param>
        /// <param name="redirectUri">Für diese App registrierten URI umleiten</param>
        /// <returns>Ein ClientContext, der bereit ist zum Aufrufen von targetUrl mit einem gültigen Zugriffstoken.</returns>
        public static ClientContext GetClientContextWithAuthorizationCode(
            string targetUrl,
            string authorizationCode,
            Uri redirectUri)
        {
            return GetClientContextWithAuthorizationCode(targetUrl, SharePointPrincipal, authorizationCode, GetRealmFromTargetUrl(new Uri(targetUrl)), redirectUri);
        }

        /// <summary>
        /// Ruft ein Zugriffstoken vom ACS mithilfe des angegebenen Autorisierungscodes ab und verwendet das Zugriffstoken zum
        /// Erstellen eines Clientkontexts.
        /// </summary>
        /// <param name="targetUrl">URL der Ziel-SharePoint-Website</param>
        /// <param name="targetPrincipalName">Name des Ziel-SharePoint-Prinzipals</param>
        /// <param name="authorizationCode">Autorisierungscode für das Abrufen des Zugriffstokens vom ACS</param>
        /// <param name="targetRealm">Zu verwendender Bereich für Namens-ID und Zielgruppe des Zugriffstokens</param>
        /// <param name="redirectUri">Für diese App registrierten URI umleiten</param>
        /// <returns>Ein ClientContext, der bereit ist zum Aufrufen von targetUrl mit einem gültigen Zugriffstoken.</returns>
        public static ClientContext GetClientContextWithAuthorizationCode(
            string targetUrl,
            string targetPrincipalName,
            string authorizationCode,
            string targetRealm,
            Uri redirectUri)
        {
            Uri targetUri = new Uri(targetUrl);

            string accessToken =
                GetAccessToken(authorizationCode, targetPrincipalName, targetUri.Authority, targetRealm, redirectUri).AccessToken;

            return GetClientContextWithAccessToken(targetUrl, accessToken);
        }

        /// <summary>
        /// Verwendet das angegebene Zugriffstoken, um einen Clientkontext zu erstellen.
        /// </summary>
        /// <param name="targetUrl">URL der Ziel-SharePoint-Website</param>
        /// <param name="accessToken">Zu verwendendes Zugriffstoken beim Aufrufen der angegebenen targetUrl</param>
        /// <returns>Ein ClientContext, der zum Aufrufen von targetUrl mit dem angegebenen Zugriffstoken bereit ist.</returns>
        public static ClientContext GetClientContextWithAccessToken(string targetUrl, string accessToken)
        {
            ClientContext clientContext = new ClientContext(targetUrl);

            clientContext.AuthenticationMode = ClientAuthenticationMode.Anonymous;
            clientContext.FormDigestHandlingEnabled = false;
            clientContext.ExecutingWebRequest +=
                delegate(object oSender, WebRequestEventArgs webRequestEventArgs)
                {
                    webRequestEventArgs.WebRequestExecutor.RequestHeaders["Authorization"] =
                        "Bearer " + accessToken;
                };

            return clientContext;
        }

        /// <summary>
        /// Ruft ein Zugriffstoken vom ACS mithilfe des angegebenen Kontexttokens ab und verwendet das Zugriffstoken,
        /// um einen Clientkontext zu erstellen.
        /// </summary>
        /// <param name="targetUrl">URL der Ziel-SharePoint-Website</param>
        /// <param name="contextTokenString">Kontexttoken erhalten von der Ziel-SharePoint-Website.</param>
        /// <param name="appHostUrl">URL-Autorität der gehosteten App.  Wenn der Wert NULL ist, wird der Wert in HostedAppHostName
        /// von web.config stattdessen verwendet.</param>
        /// <returns>Ein ClientContext, der bereit ist zum Aufrufen von targetUrl mit einem gültigen Zugriffstoken.</returns>
        public static ClientContext GetClientContextWithContextToken(
            string targetUrl,
            string contextTokenString,
            string appHostUrl)
        {
            SharePointContextToken contextToken = ReadAndValidateContextToken(contextTokenString, appHostUrl);

            Uri targetUri = new Uri(targetUrl);

            string accessToken = GetAccessToken(contextToken, targetUri.Authority).AccessToken;

            return GetClientContextWithAccessToken(targetUrl, accessToken);
        }

        /// <summary>
        /// Gibt die SharePoint-URL zurück, zu der der Browser umgeleitet werden soll, um eine Zustimmung anzufordern und
        /// einen Autorisierungscode zurückzuerhalten.
        /// </summary>
        /// <param name="contextUrl">Absolute URL der SharePoint-Website</param>
        /// <param name="scope">Durch Leerzeichen getrennte Berechtigungen, die von der SharePoint-Website in "Kurzform" angefordert werden können
        /// (Beispiel: "Web.Read Site.Write")</param>
        /// <returns>URL der OAuth-Autorisierungsseite der SharePoint-Website</returns>
        public static string GetAuthorizationUrl(string contextUrl, string scope)
        {
            return string.Format(
                "{0}{1}?IsDlg=1&client_id={2}&scope={3}&response_type=code",
                EnsureTrailingSlash(contextUrl),
                AuthorizationPage,
                ClientId,
                scope);
        }

        /// <summary>
        /// Gibt die SharePoint-URL zurück, zu der der Browser umgeleitet werden soll, um eine Zustimmung anzufordern und
        /// einen Autorisierungscode zurückzuerhalten.
        /// </summary>
        /// <param name="contextUrl">Absolute URL der SharePoint-Website</param>
        /// <param name="scope">Durch Leerzeichen getrennte Berechtigungen, die von der SharePoint-Website in "Kurzform" angefordert werden können.
        /// (Beispiel: "Web.Read Site.Write")</param>
        /// <param name="redirectUri">URI, zu dem der Browser umgeleitet werden soll, nachdem Zustimmung
        /// erteilt wurde</param>
        /// <returns>URL der OAuth-Autorisierungsseite der SharePoint-Website</returns>
        public static string GetAuthorizationUrl(string contextUrl, string scope, string redirectUri)
        {
            return string.Format(
                "{0}{1}?IsDlg=1&client_id={2}&scope={3}&response_type=code&redirect_uri={4}",
                EnsureTrailingSlash(contextUrl),
                AuthorizationPage,
                ClientId,
                scope,
                redirectUri);
        }

        /// <summary>
        /// Gibt die SharePoint-URL zurück, zu der der Browser umgeleitet werden soll, um ein neues Kontexttoken anzufordern.
        /// </summary>
        /// <param name="contextUrl">Absolute URL der SharePoint-Website</param>
        /// <param name="redirectUri">URI, zu dem der Browser mit einem Kontexttoken umgeleitet werden soll.</param>
        /// <returns>URL der Kontexttoken-Umleitungsseite der SharePoint-Website</returns>
        public static string GetAppContextTokenRequestUrl(string contextUrl, string redirectUri)
        {
            return string.Format(
                "{0}{1}?client_id={2}&redirect_uri={3}",
                EnsureTrailingSlash(contextUrl),
                RedirectPage,
                ClientId,
                redirectUri);
        }

        /// <summary>
        /// Ruft ein S2S-Zugriffstoken ab, das vom privaten Zertifikat der Anwendung für die angegebene
        /// WindowsIdentity signiert ist und für SharePoint beim targetApplicationUri beabsichtigt ist. Wenn kein Wert für "Realm" angegeben ist in der
        /// web.config, wird eine Authentifizierungsanforderung für den targetApplicationUri für die Suche ausgestellt.
        /// </summary>
        /// <param name="targetApplicationUri">URL der Ziel-SharePoint-Website</param>
        /// <param name="identity">Windows-Identität des Benutzers, für den ein Zugriffstoken erstellt werden soll.</param>
        /// <returns>Ein Zugriffstoken mit einer Zielgruppe des Zielprinzipals</returns>
        public static string GetS2SAccessTokenWithWindowsIdentity(
            Uri targetApplicationUri,
            WindowsIdentity identity)
        {
            string realm = string.IsNullOrEmpty(Realm) ? GetRealmFromTargetUrl(targetApplicationUri) : Realm;

            JsonWebTokenClaim[] claims = identity != null ? GetClaimsWithWindowsIdentity(identity) : null;

            return GetS2SAccessTokenWithClaims(targetApplicationUri.Authority, realm, claims);
        }

        /// <summary>
        /// Ruft einen S2S-Clientkontext mit einem Zugriffstoken ab, das vom privaten Zertifikat der Anwendung signiert ist für
        /// für die angegebene WindowsIdentity signiert ist und beabsichtigt ist für die Anwendung am targetApplicationUri unter Verwendung von
        /// targetRealm. Wenn in web.config kein "Realm" angegeben ist, wird eine Authentifizierungsanforderung für
        /// targetApplicationUri für die Suche ausgestellt.
        /// </summary>
        /// <param name="targetApplicationUri">URL der Ziel-SharePoint-Website</param>
        /// <param name="identity">Windows-Identität des Benutzers, für den ein Zugriffstoken erstellt werden soll.</param>
        /// <returns>Ein ClientContext, der ein Zugriffstoken mit einer Zielgruppe der Zielanwendung verwendet.</returns>
        public static ClientContext GetS2SClientContextWithWindowsIdentity(
            Uri targetApplicationUri,
            WindowsIdentity identity)
        {
            string realm = string.IsNullOrEmpty(Realm) ? GetRealmFromTargetUrl(targetApplicationUri) : Realm;

            JsonWebTokenClaim[] claims = identity != null ? GetClaimsWithWindowsIdentity(identity) : null;

            string accessToken = GetS2SAccessTokenWithClaims(targetApplicationUri.Authority, realm, claims);

            return GetClientContextWithAccessToken(targetApplicationUri.ToString(), accessToken);
        }

        /// <summary>
        /// Authentifizierungsbereich von SharePoint abrufen
        /// </summary>
        /// <param name="targetApplicationUri">URL der Ziel-SharePoint-Website</param>
        /// <returns>Zeichenfolgendarstellung der GUID des Bereichs</returns>
        public static string GetRealmFromTargetUrl(Uri targetApplicationUri)
        {
            WebRequest request = WebRequest.Create(targetApplicationUri + "/_vti_bin/client.svc");
            request.Headers.Add("Authorization: Bearer ");

            try
            {
                using (request.GetResponse())
                {
                }
            }
            catch (WebException e)
            {
                if (e.Response == null)
                {
                    return null;
                }

                string bearerResponseHeader = e.Response.Headers["WWW-Authenticate"];
                if (string.IsNullOrEmpty(bearerResponseHeader))
                {
                    return null;
                }

                const string bearer = "Bearer realm=\"";
                int bearerIndex = bearerResponseHeader.IndexOf(bearer, StringComparison.Ordinal);
                if (bearerIndex < 0)
                {
                    return null;
                }

                int realmIndex = bearerIndex + bearer.Length;

                if (bearerResponseHeader.Length >= realmIndex + 36)
                {
                    string targetRealm = bearerResponseHeader.Substring(realmIndex, 36);

                    Guid realmGuid;

                    if (Guid.TryParse(targetRealm, out realmGuid))
                    {
                        return targetRealm;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Ermittelt, ob dies eine App mit hoher Vertrauensebene ist.
        /// </summary>
        /// <returns>True, wenn dies eine App mit hoher Vertrauensebene ist.</returns>
        public static bool IsHighTrustApp()
        {
            return SigningCredentials != null;
        }

        /// <summary>
        /// Stellt sicher, dass die angegebene URL mit '/' endet, wenn sie nicht null (0) oder leer ist.
        /// </summary>
        /// <param name="url">Die URL.</param>
        /// <returns>Die URL hat die Endung '/', wenn sie nicht null (0) oder leer ist.</returns>
        public static string EnsureTrailingSlash(string url)
        {
            if (!string.IsNullOrEmpty(url) && url[url.Length - 1] != '/')
            {
                return url + "/";
            }

            return url;
        }

        #endregion

        #region private Felder

        //
        // Konfigurationskonstanten
        //

        private const string AuthorizationPage = "_layouts/15/OAuthAuthorize.aspx";
        private const string RedirectPage = "_layouts/15/AppRedirect.aspx";
        private const string AcsPrincipalName = "00000001-0000-0000-c000-000000000000";
        private const string AcsMetadataEndPointRelativeUrl = "metadata/json/1";
        private const string S2SProtocol = "OAuth2";
        private const string DelegationIssuance = "DelegationIssuance1.0";
        private const string NameIdentifierClaimType = JsonWebTokenConstants.ReservedClaims.NameIdentifier;
        private const string TrustedForImpersonationClaimType = "trustedfordelegation";
        private const string ActorTokenClaimType = JsonWebTokenConstants.ReservedClaims.ActorToken;

        //
        // Umgebungskonstanten
        //

        private static string GlobalEndPointPrefix = "accounts";
        private static string AcsHostUrl = "accesscontrol.windows.net";

        //
        // Gehostete App-Konfiguration
        //
        private static readonly string ClientId = string.IsNullOrEmpty(WebConfigurationManager.AppSettings.Get("ClientId")) ? WebConfigurationManager.AppSettings.Get("HostedAppName") : WebConfigurationManager.AppSettings.Get("ClientId");
        private static readonly string IssuerId = string.IsNullOrEmpty(WebConfigurationManager.AppSettings.Get("IssuerId")) ? ClientId : WebConfigurationManager.AppSettings.Get("IssuerId");
        private static readonly string HostedAppHostNameOverride = WebConfigurationManager.AppSettings.Get("HostedAppHostNameOverride");
        private static readonly string HostedAppHostName = WebConfigurationManager.AppSettings.Get("HostedAppHostName");
        private static readonly string ClientSecret = string.IsNullOrEmpty(WebConfigurationManager.AppSettings.Get("ClientSecret")) ? WebConfigurationManager.AppSettings.Get("HostedAppSigningKey") : WebConfigurationManager.AppSettings.Get("ClientSecret");
        private static readonly string SecondaryClientSecret = WebConfigurationManager.AppSettings.Get("SecondaryClientSecret");
        private static readonly string Realm = WebConfigurationManager.AppSettings.Get("Realm");
        private static readonly string ServiceNamespace = WebConfigurationManager.AppSettings.Get("Realm");

        private static readonly string ClientSigningCertificatePath = WebConfigurationManager.AppSettings.Get("ClientSigningCertificatePath");
        private static readonly string ClientSigningCertificatePassword = WebConfigurationManager.AppSettings.Get("ClientSigningCertificatePassword");
        private static readonly X509Certificate2 ClientCertificate = (string.IsNullOrEmpty(ClientSigningCertificatePath) || string.IsNullOrEmpty(ClientSigningCertificatePassword)) ? null : new X509Certificate2(ClientSigningCertificatePath, ClientSigningCertificatePassword);
        private static readonly X509SigningCredentials SigningCredentials = (ClientCertificate == null) ? null : new X509SigningCredentials(ClientCertificate, SecurityAlgorithms.RsaSha256Signature, SecurityAlgorithms.Sha256Digest);

        #endregion

        #region private Methoden

        private static ClientContext CreateAcsClientContextForUrl(SPRemoteEventProperties properties, Uri sharepointUrl)
        {
            string contextTokenString = properties.ContextToken;

            if (String.IsNullOrEmpty(contextTokenString))
            {
                return null;
            }

            SharePointContextToken contextToken = ReadAndValidateContextToken(contextTokenString, OperationContext.Current.IncomingMessageHeaders.To.Host);
            string accessToken = GetAccessToken(contextToken, sharepointUrl.Authority).AccessToken;

            return GetClientContextWithAccessToken(sharepointUrl.ToString(), accessToken);
        }

        private static string GetAcsMetadataEndpointUrl()
        {
            return Path.Combine(GetAcsGlobalEndpointUrl(), AcsMetadataEndPointRelativeUrl);
        }

        private static string GetFormattedPrincipal(string principalName, string hostName, string realm)
        {
            if (!String.IsNullOrEmpty(hostName))
            {
                return String.Format(CultureInfo.InvariantCulture, "{0}/{1}@{2}", principalName, hostName, realm);
            }

            return String.Format(CultureInfo.InvariantCulture, "{0}@{1}", principalName, realm);
        }

        private static string GetAcsPrincipalName(string realm)
        {
            return GetFormattedPrincipal(AcsPrincipalName, new Uri(GetAcsGlobalEndpointUrl()).Host, realm);
        }

        private static string GetAcsGlobalEndpointUrl()
        {
            return String.Format(CultureInfo.InvariantCulture, "https://{0}.{1}/", GlobalEndPointPrefix, AcsHostUrl);
        }

        private static JsonWebSecurityTokenHandler CreateJsonWebSecurityTokenHandler()
        {
            JsonWebSecurityTokenHandler handler = new JsonWebSecurityTokenHandler();
            handler.Configuration = new SecurityTokenHandlerConfiguration();
            handler.Configuration.AudienceRestriction = new AudienceRestriction(AudienceUriMode.Never);
            handler.Configuration.CertificateValidator = X509CertificateValidator.None;

            List<byte[]> securityKeys = new List<byte[]>();
            securityKeys.Add(Convert.FromBase64String(ClientSecret));
            if (!string.IsNullOrEmpty(SecondaryClientSecret))
            {
                securityKeys.Add(Convert.FromBase64String(SecondaryClientSecret));
            }

            List<SecurityToken> securityTokens = new List<SecurityToken>();
            securityTokens.Add(new MultipleSymmetricKeySecurityToken(securityKeys));

            handler.Configuration.IssuerTokenResolver =
                SecurityTokenResolver.CreateDefaultSecurityTokenResolver(
                new ReadOnlyCollection<SecurityToken>(securityTokens),
                false);
            SymmetricKeyIssuerNameRegistry issuerNameRegistry = new SymmetricKeyIssuerNameRegistry();
            foreach (byte[] securitykey in securityKeys)
            {
                issuerNameRegistry.AddTrustedIssuer(securitykey, GetAcsPrincipalName(ServiceNamespace));
            }
            handler.Configuration.IssuerNameRegistry = issuerNameRegistry;
            return handler;
        }

        private static string GetS2SAccessTokenWithClaims(
            string targetApplicationHostName,
            string targetRealm,
            IEnumerable<JsonWebTokenClaim> claims)
        {
            return IssueToken(
                ClientId,
                IssuerId,
                targetRealm,
                SharePointPrincipal,
                targetRealm,
                targetApplicationHostName,
                true,
                claims,
                claims == null);
        }

        private static JsonWebTokenClaim[] GetClaimsWithWindowsIdentity(WindowsIdentity identity)
        {
            JsonWebTokenClaim[] claims = new JsonWebTokenClaim[]
            {
                new JsonWebTokenClaim(NameIdentifierClaimType, identity.User.Value.ToLower()),
                new JsonWebTokenClaim("nii", "urn:office:idp:activedirectory")
            };
            return claims;
        }

        private static string IssueToken(
            string sourceApplication,
            string issuerApplication,
            string sourceRealm,
            string targetApplication,
            string targetRealm,
            string targetApplicationHostName,
            bool trustedForDelegation,
            IEnumerable<JsonWebTokenClaim> claims,
            bool appOnly = false)
        {
            if (null == SigningCredentials)
            {
                throw new InvalidOperationException("SigningCredentials was not initialized");
            }

            #region Actor-Token

            string issuer = string.IsNullOrEmpty(sourceRealm) ? issuerApplication : string.Format("{0}@{1}", issuerApplication, sourceRealm);
            string nameid = string.IsNullOrEmpty(sourceRealm) ? sourceApplication : string.Format("{0}@{1}", sourceApplication, sourceRealm);
            string audience = string.Format("{0}/{1}@{2}", targetApplication, targetApplicationHostName, targetRealm);

            List<JsonWebTokenClaim> actorClaims = new List<JsonWebTokenClaim>();
            actorClaims.Add(new JsonWebTokenClaim(JsonWebTokenConstants.ReservedClaims.NameIdentifier, nameid));
            if (trustedForDelegation && !appOnly)
            {
                actorClaims.Add(new JsonWebTokenClaim(TrustedForImpersonationClaimType, "true"));
            }

            // Token erstellen
            JsonWebSecurityToken actorToken = new JsonWebSecurityToken(
                issuer: issuer,
                audience: audience,
                validFrom: DateTime.UtcNow,
                validTo: DateTime.UtcNow.Add(HighTrustAccessTokenLifetime),
                signingCredentials: SigningCredentials,
                claims: actorClaims);

            string actorTokenString = new JsonWebSecurityTokenHandler().WriteTokenAsString(actorToken);

            if (appOnly)
            {
                // Zugriffstoken nur für Anwendungen ist für delegierten Fall identisch mit actor-Token.
                return actorTokenString;
            }

            #endregion Actor token

            #region Outer-Token

            List<JsonWebTokenClaim> outerClaims = null == claims ? new List<JsonWebTokenClaim>() : new List<JsonWebTokenClaim>(claims);
            outerClaims.Add(new JsonWebTokenClaim(ActorTokenClaimType, actorTokenString));

            JsonWebSecurityToken jsonToken = new JsonWebSecurityToken(
                nameid, // outer-Tokenaussteller sollte mit Namens-ID für actor-Token übereinstimmen.
                audience,
                DateTime.UtcNow,
                DateTime.UtcNow.Add(HighTrustAccessTokenLifetime),
                outerClaims);

            string accessToken = new JsonWebSecurityTokenHandler().WriteTokenAsString(jsonToken);

            #endregion Outer token

            return accessToken;
        }

        #endregion

        #region AcsMetadataParser

        // Diese Klasse wird zum Abrufen des MetaData-Dokuments vom globalen STS-Endpunkt verwendet. Sie enthält
        // Methoden zum Analysieren des MetaData-Dokuments und zum Abrufen von Endpunkten und dem STS-Zertifikat.
        public static class AcsMetadataParser
        {
            public static X509Certificate2 GetAcsSigningCert(string realm)
            {
                JsonMetadataDocument document = GetMetadataDocument(realm);

                if (null != document.keys && document.keys.Count > 0)
                {
                    JsonKey signingKey = document.keys[0];

                    if (null != signingKey && null != signingKey.keyValue)
                    {
                        return new X509Certificate2(Encoding.UTF8.GetBytes(signingKey.keyValue.value));
                    }
                }

                throw new Exception("Metadata document does not contain ACS signing certificate.");
            }

            public static string GetDelegationServiceUrl(string realm)
            {
                JsonMetadataDocument document = GetMetadataDocument(realm);

                JsonEndpoint delegationEndpoint = document.endpoints.SingleOrDefault(e => e.protocol == DelegationIssuance);

                if (null != delegationEndpoint)
                {
                    return delegationEndpoint.location;
                }
                throw new Exception("Metadata document does not contain Delegation Service endpoint Url");
            }

            private static JsonMetadataDocument GetMetadataDocument(string realm)
            {
                string acsMetadataEndpointUrlWithRealm = String.Format(CultureInfo.InvariantCulture, "{0}?realm={1}",
                                                                       GetAcsMetadataEndpointUrl(),
                                                                       realm);
                byte[] acsMetadata;
                using (WebClient webClient = new WebClient())
                {

                    acsMetadata = webClient.DownloadData(acsMetadataEndpointUrlWithRealm);
                }
                string jsonResponseString = Encoding.UTF8.GetString(acsMetadata);

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                JsonMetadataDocument document = serializer.Deserialize<JsonMetadataDocument>(jsonResponseString);

                if (null == document)
                {
                    throw new Exception("No metadata document found at the global endpoint " + acsMetadataEndpointUrlWithRealm);
                }

                return document;
            }

            public static string GetStsUrl(string realm)
            {
                JsonMetadataDocument document = GetMetadataDocument(realm);

                JsonEndpoint s2sEndpoint = document.endpoints.SingleOrDefault(e => e.protocol == S2SProtocol);

                if (null != s2sEndpoint)
                {
                    return s2sEndpoint.location;
                }

                throw new Exception("Metadata document does not contain STS endpoint url");
            }

            private class JsonMetadataDocument
            {
                public string serviceName { get; set; }
                public List<JsonEndpoint> endpoints { get; set; }
                public List<JsonKey> keys { get; set; }
            }

            private class JsonEndpoint
            {
                public string location { get; set; }
                public string protocol { get; set; }
                public string usage { get; set; }
            }

            private class JsonKeyValue
            {
                public string type { get; set; }
                public string value { get; set; }
            }

            private class JsonKey
            {
                public string usage { get; set; }
                public JsonKeyValue keyValue { get; set; }
            }
        }

        #endregion
    }

    /// <summary>
    /// Ein JsonWebSecurityToken, das von SharePoint zum Authentifizieren einer Anwendung eines Drittanbieters und zum Zulassen von Rückrufen mithilfe eines refresh-Tokens erzeugt wird.
    /// </summary>
    public class SharePointContextToken : JsonWebSecurityToken
    {
        public static SharePointContextToken Create(JsonWebSecurityToken contextToken)
        {
            return new SharePointContextToken(contextToken.Issuer, contextToken.Audience, contextToken.ValidFrom, contextToken.ValidTo, contextToken.Claims);
        }

        public SharePointContextToken(string issuer, string audience, DateTime validFrom, DateTime validTo, IEnumerable<JsonWebTokenClaim> claims)
            : base(issuer, audience, validFrom, validTo, claims)
        {
        }

        public SharePointContextToken(string issuer, string audience, DateTime validFrom, DateTime validTo, IEnumerable<JsonWebTokenClaim> claims, SecurityToken issuerToken, JsonWebSecurityToken actorToken)
            : base(issuer, audience, validFrom, validTo, claims, issuerToken, actorToken)
        {
        }

        public SharePointContextToken(string issuer, string audience, DateTime validFrom, DateTime validTo, IEnumerable<JsonWebTokenClaim> claims, SigningCredentials signingCredentials)
            : base(issuer, audience, validFrom, validTo, claims, signingCredentials)
        {
        }

        public string NameId
        {
            get
            {
                return GetClaimValue(this, "nameid");
            }
        }

        /// <summary>
        /// Der Prinzipalnamensabschnitt des "appctxsender"-Anspruchs des Kontexttokens.
        /// </summary>
        public string TargetPrincipalName
        {
            get
            {
                string appctxsender = GetClaimValue(this, "appctxsender");

                if (appctxsender == null)
                {
                    return null;
                }

                return appctxsender.Split('@')[0];
            }
        }

        /// <summary>
        /// Der "refreshtoken"-Anspruch des Kontexttokens
        /// </summary>
        public string RefreshToken
        {
            get
            {
                return GetClaimValue(this, "refreshtoken");
            }
        }

        /// <summary>
        /// Der "CacheKey"-Anspruch des Kontexttokens
        /// </summary>
        public string CacheKey
        {
            get
            {
                string appctx = GetClaimValue(this, "appctx");
                if (appctx == null)
                {
                    return null;
                }

                ClientContext ctx = new ClientContext("http://tempuri.org");
                Dictionary<string, object> dict = (Dictionary<string, object>)ctx.ParseObjectFromJsonString(appctx);
                string cacheKey = (string)dict["CacheKey"];

                return cacheKey;
            }
        }

        /// <summary>
        /// Der "SecurityTokenServiceUri"-Anspruch des Kontexttokens
        /// </summary>
        public string SecurityTokenServiceUri
        {
            get
            {
                string appctx = GetClaimValue(this, "appctx");
                if (appctx == null)
                {
                    return null;
                }

                ClientContext ctx = new ClientContext("http://tempuri.org");
                Dictionary<string, object> dict = (Dictionary<string, object>)ctx.ParseObjectFromJsonString(appctx);
                string securityTokenServiceUri = (string)dict["SecurityTokenServiceUri"];

                return securityTokenServiceUri;
            }
        }

        /// <summary>
        /// Der realm-Abschnitt des "audience"-Anspruchs des Kontexttokens
        /// </summary>
        public string Realm
        {
            get
            {
                string aud = Audience;
                if (aud == null)
                {
                    return null;
                }

                string tokenRealm = aud.Substring(aud.IndexOf('@') + 1);

                return tokenRealm;
            }
        }

        private static string GetClaimValue(JsonWebSecurityToken token, string claimType)
        {
            if (token == null)
            {
                throw new ArgumentNullException("token");
            }

            foreach (JsonWebTokenClaim claim in token.Claims)
            {
                if (StringComparer.Ordinal.Equals(claim.ClaimType, claimType))
                {
                    return claim.Value;
                }
            }

            return null;
        }

    }

    /// <summary>
    /// Stellt ein Sicherheitstoken dar, das mehrere Sicherheitsschlüssel enthält, die mithilfe von symmetrischen Algorithmen erzeugt werden.
    /// </summary>
    public class MultipleSymmetricKeySecurityToken : SecurityToken
    {
        /// <summary>
        /// Initialisiert eine neue Instanz der MultipleSymmetricKeySecurityToken-Klasse.
        /// </summary>
        /// <param name="keys">Eine Enumeration von Bytearrays, die die symmetrischen Schlüssel enthalten.</param>
        public MultipleSymmetricKeySecurityToken(IEnumerable<byte[]> keys)
            : this(UniqueId.CreateUniqueId(), keys)
        {
        }

        /// <summary>
        /// Initialisiert eine neue Instanz der MultipleSymmetricKeySecurityToken-Klasse.
        /// </summary>
        /// <param name="tokenId">Der eindeutige Bezeichner des Sicherheitstokens</param>
        /// <param name="keys">Eine Enumeration von Bytearrays, die die symmetrischen Schlüssel enthalten.</param>
        public MultipleSymmetricKeySecurityToken(string tokenId, IEnumerable<byte[]> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException("keys");
            }

            if (String.IsNullOrEmpty(tokenId))
            {
                throw new ArgumentException("Value cannot be a null or empty string.", "tokenId");
            }

            foreach (byte[] key in keys)
            {
                if (key.Length <= 0)
                {
                    throw new ArgumentException("The key length must be greater then zero.", "keys");
                }
            }

            id = tokenId;
            effectiveTime = DateTime.UtcNow;
            securityKeys = CreateSymmetricSecurityKeys(keys);
        }

        /// <summary>
        /// Ruft den eindeutigen Bezeichner des Sicherheitstokens ab.
        /// </summary>
        public override string Id
        {
            get
            {
                return id;
            }
        }

        /// <summary>
        /// Ruft kryptografische Schlüssel ab, die dem Sicherheitstoken zugeordnet sind.
        /// </summary>
        public override ReadOnlyCollection<SecurityKey> SecurityKeys
        {
            get
            {
                return securityKeys.AsReadOnly();
            }
        }

        /// <summary>
        /// Ruft den ersten Zeitpunkt ab, zu dem das Sicherheitstoken gültig ist.
        /// </summary>
        public override DateTime ValidFrom
        {
            get
            {
                return effectiveTime;
            }
        }

        /// <summary>
        /// Ruft den letzten Zeitpunkt ab, zu dem das Sicherheitstoken gültig ist.
        /// </summary>
        public override DateTime ValidTo
        {
            get
            {
                // Niemals ablaufen
                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// Gibt einen Wert zurück, der angibt, ob die Schlüsselkennung für diese Instanz zur angegebenen Schlüsselkennung aufgelöst werden kann.
        /// </summary>
        /// <param name="keyIdentifierClause">Eine SecurityKeyIdentifierClause zum Vergleichen mit dieser Instanz</param>
        /// <returns>"True", wenn keyIdentifierClause eine SecurityKeyIdentifierClause ist und denselben eindeutigen Bezeichner wie die ID-Eigenschaft besitzt, andernfalls "False".</returns>
        public override bool MatchesKeyIdentifierClause(SecurityKeyIdentifierClause keyIdentifierClause)
        {
            if (keyIdentifierClause == null)
            {
                throw new ArgumentNullException("keyIdentifierClause");
            }

            // Da dies ein symmetrisches Token ist und wir keine IDs zur Unterscheidung von Token besitzen, überprüfen wir nur
            // Anwesenheit eines SymmetricIssuerKeyIdentifier. Die tatsächliche Zuordnung zum Aussteller findet später statt,
            // wenn der Schlüssel mit dem Aussteller übereinstimmt.
            if (keyIdentifierClause is SymmetricIssuerKeyIdentifierClause)
            {
                return true;
            }
            return base.MatchesKeyIdentifierClause(keyIdentifierClause);
        }

        #region private Member

        private List<SecurityKey> CreateSymmetricSecurityKeys(IEnumerable<byte[]> keys)
        {
            List<SecurityKey> symmetricKeys = new List<SecurityKey>();
            foreach (byte[] key in keys)
            {
                symmetricKeys.Add(new InMemorySymmetricSecurityKey(key));
            }
            return symmetricKeys;
        }

        private string id;
        private DateTime effectiveTime;
        private List<SecurityKey> securityKeys;

        #endregion
    }
}
