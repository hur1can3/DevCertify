// Program.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Linq; // Added for LINQ operations on args

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Localhost SSL Certificate CLI Tool (.NET 9)");
        Console.WriteLine("--------------------------------------------");

        // Determine if OpenSSL method is requested
        bool useOpenSsl = args.Contains("--openssl", StringComparer.OrdinalIgnoreCase);
        string dnsName = "localhost"; // Default DNS name

        // Extract DNS name if provided (for both mkcert and openssl)
        // Look for a non-flag argument after --openssl or as a standalone argument
        var nonFlagArgs = args.Where(arg => !arg.StartsWith("--")).ToList();
        if (nonFlagArgs.Any())
        {
            dnsName = nonFlagArgs.Last(); // Take the last non-flag argument as the DNS name
        }

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            DisplayHelp();
            return; // Exit after displaying help
        }

        if (useOpenSsl)
        {
            Console.WriteLine($"Using OpenSSL method to create certificate for: {dnsName}");
            if (!IsOpensslInstalled())
            {
                Console.WriteLine("OpenSSL is not found on your system. Please install OpenSSL manually and try again.");
                Console.WriteLine("You can find instructions for your OS online (e.g., Homebrew for macOS, apt for Debian/Ubuntu, Scoop/Chocolatey for Windows).");
                return;
            }

            if (!await GenerateOpenSslCertificate(dnsName))
            {
                Console.WriteLine("Failed to generate certificate using OpenSSL.");
                return;
            }
        }
        else // Default to mkcert method
        {
            // Check and install mkcert if not found
            if (!IsMkcertInstalled())
            {
                Console.WriteLine("mkcert is not found on your system. Attempting to install it...");
                if (!await InstallMkcert())
                {
                    Console.WriteLine("Failed to install mkcert. Please install it manually and try again.");
                    Console.WriteLine("You can find instructions at: https://github.com/FiloSottile/mkcert");
                    return;
                }
                Console.WriteLine("mkcert installed successfully.");
            }
            else
            {
                Console.WriteLine("mkcert is installed. Proceeding with certificate generation...");
            }

            // Step 1: Install the local CA (if not already installed)
            Console.WriteLine("\nStep 1: Installing local mkcert CA...");
            // mkcert -install requires admin/sudo, so always attempt with elevated privileges
            if (!await RunMkcertCommand("-install", true))
            {
                Console.WriteLine("Failed to install mkcert CA. Please ensure you have necessary permissions (e.g., run as administrator/sudo).");
                return;
            }
            Console.WriteLine("Local mkcert CA installed successfully.");

            // Step 2: Generate certificate for localhost
            Console.WriteLine("\nStep 2: Generating certificate for 'localhost'...");
            string certFileName = $"{dnsName}.p12"; // Use dnsName for filename
            string pfxFileName = $"{dnsName}.pfx"; // Use dnsName for filename

            // Clean up old files if they exist to avoid conflicts
            try
            {
                if (File.Exists(certFileName)) File.Delete(certFileName);
                if (File.Exists(pfxFileName)) File.Delete(pfxFileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not clean up old certificate files. Error: {ex.Message}");
            }

            if (!await RunMkcertCommand($"-pkcs12 {dnsName}", false)) // Use dnsName here
            {
                Console.WriteLine($"Failed to generate {dnsName} certificate.");
                return;
            }
            Console.WriteLine($"Certificate '{certFileName}' generated successfully.");

            // Step 3: Rename .p12 to .pfx for IIS compatibility (Windows specific, but harmless elsewhere)
            try
            {
                if (File.Exists(certFileName))
                {
                    File.Move(certFileName, pfxFileName);
                    Console.WriteLine($"Renamed '{certFileName}' to '{pfxFileName}'.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not rename {certFileName} to {pfxFileName}. Error: {ex.Message}");
            }

            Console.WriteLine("\nCertificate generation complete!");
            Console.WriteLine($"A trusted SSL certificate for '{dnsName}' has been created as '{pfxFileName}' in the current directory.");

            // Step 4: Windows-specific IIS integration
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("\nStep 4: Attempting to configure IIS binding for 'Default Web Site'...");
                string iisSiteName = "Default Web Site"; // Default IIS site name
                string pfxPassword = "changeit"; // Default mkcert PKCS#12 password

                if (await ConfigureIisBinding(Path.GetFullPath(pfxFileName), pfxPassword, iisSiteName, dnsName))
                {
                    Console.WriteLine($"Successfully configured IIS for '{iisSiteName}' with the new certificate.");
                    Console.WriteLine("You might need to restart IIS (iisreset) or your application pool for changes to take effect.");
                }
                else
                {
                    Console.WriteLine("Failed to automatically configure IIS. You may need to do this manually.");
                    Console.WriteLine("  1. Open 'Manage Computer Certificates' (certlm.msc).");
                    Console.WriteLine("  2. Navigate to 'Personal' -> 'Certificates'.");
                    Console.WriteLine("  3. Right-click 'Certificates' -> All Tasks -> Import...");
                    Console.WriteLine($"  4. Browse to '{Path.GetFullPath(pfxFileName)}', select it, and follow the wizard.");
                    Console.WriteLine("     (Default password for mkcert PKCS#12 files is 'changeit')");
                    Console.WriteLine("  5. For IIS, open IIS Manager, select your site, then 'Bindings...', add/edit HTTPS binding, and select the imported certificate.");
                }
            }
            else
            {
                Console.WriteLine("\nNext Steps (Manual Import Required for your OS/Web Server):");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Console.WriteLine("  For macOS:");
                    Console.WriteLine("  1. Double-click the generated '{pfxFileName}' file.");
                    Console.WriteLine("  2. Keychain Access will open. Select 'login' or 'System' keychain.");
                    Console.WriteLine("  3. Enter the password 'changeit' when prompted.");
                    Console.WriteLine("  4. Find the certificate, double-click it, expand 'Trust', and set 'When using this certificate:' to 'Always Trust'.");
                    Console.WriteLine("  5. If using a web server like Nginx or Apache, you'll need to configure it to use the generated certificate and key.");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Console.WriteLine("  For Linux:");
                    Console.WriteLine("  mkcert usually handles trust for common browsers (Firefox, Chrome) automatically via the system trust store.");
                    Console.WriteLine("  You can use the generated '{pfxFileName}' with your web server (e.g., Nginx, Apache) configuration.");
                    Console.WriteLine("  If you encounter issues with browser trust, you might need to manually import the CA certificate (usually found in ~/.local/share/mkcert/)");
                    Console.WriteLine("  into your browser's trust store or system-wide trust store (e.g., /usr/local/share/ca-certificates/ for Debian/Ubuntu based systems).");
                }
            }
        }

        Console.WriteLine("\nRemember to restart your browser or web server for changes to take effect.");
    }

    /// <summary>
    /// Checks if mkcert is installed by trying to run 'mkcert --version'.
    /// </summary>
    /// <returns>True if mkcert is found, false otherwise.</returns>
    private static bool IsMkcertInstalled()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "mkcert";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                process.WaitForExit(5000); // Wait up to 5 seconds
                return process.ExitCode == 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if OpenSSL is installed.
    /// </summary>
    private static bool IsOpensslInstalled()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "openssl";
                process.StartInfo.Arguments = "version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                process.WaitForExit(5000); // Wait up to 5 seconds
                return process.ExitCode == 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to install mkcert using platform-specific package managers.
    /// </summary>
    /// <returns>True if mkcert was successfully installed or already present, false otherwise.</returns>
    private static async Task<bool> InstallMkcert()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("  Attempting to install mkcert via Chocolatey...");
            if (IsChocolateyInstalled())
            {
                if (await RunProcessAsync("choco", "install mkcert -y", true))
                {
                    return true;
                }
                Console.WriteLine("  Chocolatey installation failed or mkcert not found via Chocolatey.");
            }
            else
            {
                Console.WriteLine("  Chocolatey is not installed. Skipping Chocolatey and trying Scoop...");
            }

            Console.WriteLine("  Attempting to install mkcert via Scoop...");
            if (IsScoopInstalled())
            {
                // Scoop install mkcert might require admin for some parts, but scoop itself is user-level.
                // mkcert -install will handle the admin part for trust.
                if (await RunProcessAsync("scoop", "install mkcert", false)) // Scoop install generally doesn't need admin
                {
                    return true;
                }
                Console.WriteLine("  Scoop installation failed or mkcert not found via Scoop.");
            }
            else
            {
                Console.WriteLine("  Scoop is not installed.");
            }

            Console.WriteLine("\n  Please install mkcert manually on Windows:");
            Console.WriteLine("  - Chocolatey: Install choco (https://chocolatey.org/install), then 'choco install mkcert'");
            Console.WriteLine("  - Scoop: Install scoop (https://scoop.sh/), then 'scoop install mkcert'");
            return false;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine("  Attempting to install mkcert via Homebrew...");
            if (IsHomebrewInstalled())
            {
                if (await RunProcessAsync("brew", "install mkcert", false))
                {
                    return true;
                }
                Console.WriteLine("  Homebrew installation failed or mkcert not found via Homebrew.");
            }
            else
            {
                Console.WriteLine("  Homebrew is not installed. Please install Homebrew first (https://brew.sh/).");
            }
            Console.WriteLine("\n  Please install mkcert manually on macOS if the automatic attempt failed.");
            Console.WriteLine("  - Homebrew: Install brew (https://brew.sh/), then 'brew install mkcert'");
            return false;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("  For Linux, automatic installation is complex due to varied package managers.");
            Console.WriteLine("  Please install mkcert manually using your distribution's package manager (e.g., sudo apt install mkcert) or from GitHub.");
            Console.WriteLine("  Instructions: https://github.com/FiloSottile/mkcert");
            return false; // Cannot reliably automate for all Linux distros
        }
        return false;
    }

    /// <summary>
    /// Checks if Chocolatey is installed.
    /// </summary>
    private static bool IsChocolateyInstalled()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "choco";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Checks if Scoop is installed.
    /// </summary>
    private static bool IsScoopInstalled()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "scoop";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Checks if Homebrew is installed.
    /// </summary>
    private static bool IsHomebrewInstalled()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "brew";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Runs an mkcert command.
    /// </summary>
    /// <param name="arguments">Arguments to pass to mkcert.</param>
    /// <param name="runAsAdmin">Whether to attempt to run the process with administrative privileges (primarily for Windows).</param>
    /// <returns>True if the command succeeded, false otherwise.</returns>
    private static async Task<bool> RunMkcertCommand(string arguments, bool runAsAdmin)
    {
        return await RunProcessAsync("mkcert", arguments, runAsAdmin);
    }

    /// <summary>
    /// Runs an external process and captures its output.
    /// </summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Arguments for the executable.</param>
    /// <param name="runAsAdmin">Whether to attempt to run the process with administrative privileges (primarily for Windows).</param>
    /// <returns>True if the command succeeded, false otherwise.</returns>
    private static async Task<bool> RunProcessAsync(string fileName, string arguments, bool runAsAdmin)
    {
        using (Process process = new Process())
        {
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true; // Keep this true for cleaner execution

            if (runAsAdmin && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.Verb = "runas"; // Request UAC prompt
                // Cannot redirect streams when UseShellExecute is true
                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.RedirectStandardError = false;
            }
            else
            {
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
            }

            try
            {
                process.Start();

                string output = "";
                string error = "";

                if (!process.StartInfo.UseShellExecute) // Only read streams if not using shell execute
                {
                    output = await process.StandardOutput.ReadToEndAsync();
                    error = await process.StandardError.ReadToEndAsync();
                }
                else
                {
                    Console.WriteLine($"  (Note: Running '{fileName}' with elevated privileges, output might not be captured directly by the tool.)");
                }

                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine($"  {fileName} output:");
                    Console.WriteLine(output);
                }
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine($"  {fileName} error:");
                    Console.WriteLine(error);
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running command '{fileName} {arguments}': {ex.Message}");
                if (runAsAdmin && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ex.Message.Contains("The operation was canceled by the user"))
                {
                    Console.WriteLine("User cancelled the UAC prompt. Please accept the prompt to proceed.");
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Configures IIS binding on Windows using PowerShell.
    /// This method requires administrator privileges.
    /// </summary>
    /// <param name="pfxPath">Full path to the .pfx certificate file.</param>
    /// <param name="pfxPassword">Password for the .pfx file (mkcert default is 'changeit').</param>
    /// <param name="siteName">The name of the IIS website to configure.</param>
    /// <param name="dnsName">The DNS name to bind (e.g., localhost).</param>
    /// <returns>True if IIS configuration was successful, false otherwise.</returns>
    private static async Task<bool> ConfigureIisBinding(string pfxPath, string pfxPassword, string siteName, string dnsName)
    {
        // PowerShell script to import PFX and bind to IIS
        string psScript = $@"
            $ErrorActionPreference = 'Stop'
            try {{
                # Check if IISAdministration module is installed
                if (-not (Get-Module -ListAvailable -Name IISAdministration)) {{
                    Write-Error 'IISAdministration PowerShell module is not installed. Please install it (e.g., Install-Module IISAdministration) and try again.'
                    exit 1
                }}

                # Import the PFX certificate into the Personal store
                Write-Host 'Importing certificate into Personal store...'
                Import-PfxCertificate -FilePath '{pfxPath}' -CertStoreLocation Cert:\LocalMachine\My -Password '{pfxPassword}' -ErrorAction Stop

                # Get the newly imported certificate by its subject (assuming localhost or specified DNS name)
                $cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {{ $_.Subject -like '*CN={dnsName}*' }} | Select-Object -First 1
                if (-not $cert) {{
                    Write-Error 'Could not find the imported certificate in Personal store with CN={dnsName}.'
                    exit 1
                }}
                $thumbprint = $cert.Thumbprint
                Write-Host 'Certificate thumbprint found: '$thumbprint

                $bindingInfo = '*:443:{dnsName}'

                # Remove existing HTTPS binding for a clean re-run.
                # Use -ErrorAction SilentlyContinue to prevent errors if binding doesn't exist.
                Write-Host 'Checking for existing HTTPS bindings for {siteName}...'
                Get-IISSiteBinding -Name '{siteName}' -Protocol https -ErrorAction SilentlyContinue | Where-Object {{ $_.BindingInformation -eq $bindingInfo }} | Remove-IISSiteBinding -ErrorAction SilentlyContinue
                Write-Host 'Existing HTTPS binding removed if found.'

                # Add the new HTTPS binding
                Write-Host 'Adding new HTTPS binding to {siteName}...'
                New-IISSiteBinding -Name '{siteName}' -BindingInformation $bindingInfo -Protocol https -CertificateThumbPrint $thumbprint -CertStoreLocation 'Cert:\LocalMachine\My' -SslFlag 'None' -ErrorAction Stop
                Write-Host 'IIS binding configured successfully.'
                exit 0
            }}
            catch {{
                Write-Error $_.Exception.Message
                exit 1
            }}
        ";

        // Execute PowerShell script with admin privileges
        Console.WriteLine("  Executing PowerShell script for IIS configuration...");
        return await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"", true);
    }

    /// <summary>
    /// Generates a self-signed SSL certificate using OpenSSL with SANs.
    /// </summary>
    /// <param name="dnsName">The primary DNS name for the certificate (e.g., "localhost").</param>
    /// <returns>True if certificate generation was successful, false otherwise.</returns>
    private static async Task<bool> GenerateOpenSslCertificate(string dnsName)
    {
        Console.WriteLine($"\nGenerating OpenSSL certificate for '{dnsName}'...");

        string keyFileName = $"{dnsName}.key";
        string csrFileName = $"{dnsName}.csr";
        string certFileName = $"{dnsName}.crt";
        string pfxFileName = $"{dnsName}.pfx";
        string opensslConfigFileName = "openssl.cfg";

        // Clean up old files
        try
        {
            if (File.Exists(keyFileName)) File.Delete(keyFileName);
            if (File.Exists(csrFileName)) File.Delete(csrFileName);
            if (File.Exists(certFileName)) File.Delete(certFileName);
            if (File.Exists(pfxFileName)) File.Delete(pfxFileName);
            if (File.Exists(opensslConfigFileName)) File.Delete(opensslConfigFileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not clean up old certificate files. Error: {ex.Message}");
        }

        // Prepare openssl.cfg with SANs
        string opensslConfigContent = $@"
[req]
distinguished_name = req_distinguished_name
x509_extensions = v3_req
prompt = no

[req_distinguished_name]
C = US
ST = State
L = City
O = DevCertify
OU = Development
CN = {dnsName}

[v3_req]
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = @alt_names

[alt_names]
DNS.1 = {dnsName}
DNS.2 = localhost # Always include localhost for convenience
IP.1 = 127.0.0.1 # Always include loopback IP
";
        try
        {
            await File.WriteAllTextAsync(opensslConfigFileName, opensslConfigContent);
            Console.WriteLine($"  Created OpenSSL config file: {opensslConfigFileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating OpenSSL config file: {ex.Message}");
            return false;
        }

        // 1. Generate Private Key
        Console.WriteLine("  Generating private key (2048-bit RSA)...");
        if (!await RunProcessAsync("openssl", $"genrsa -out {keyFileName} 2048", false))
        {
            Console.WriteLine("  Failed to generate private key.");
            return false;
        }

        // 2. Generate Certificate Signing Request (CSR)
        Console.WriteLine("  Generating Certificate Signing Request (CSR)...");
        if (!await RunProcessAsync("openssl", $"req -new -key {keyFileName} -out {csrFileName} -config {opensslConfigFileName}", false))
        {
            Console.WriteLine("  Failed to generate CSR.");
            return false;
        }

        // 3. Generate Self-Signed Certificate
        Console.WriteLine("  Generating self-signed certificate (valid for 10 years)...");
        if (!await RunProcessAsync("openssl", $"x509 -req -days 3650 -in {csrFileName} -signkey {keyFileName} -out {certFileName} -extensions v3_req -config {opensslConfigFileName}", false))
        {
            Console.WriteLine("  Failed to generate self-signed certificate.");
            return false;
        }

        // 4. Export to PFX
        Console.WriteLine("  Exporting certificate to PFX format...");
        // OpenSSL requires a password for PFX export; using a default for automation
        string pfxExportPassword = "changeit";
        if (!await RunProcessAsync("openssl", $"pkcs12 -export -out {pfxFileName} -inkey {keyFileName} -in {certFileName} -password pass:{pfxExportPassword}", false))
        {
            Console.WriteLine("  Failed to export certificate to PFX.");
            return false;
        }

        Console.WriteLine($"\nOpenSSL certificate generation complete! '{pfxFileName}' created in the current directory.");
        Console.WriteLine("\n*** IMPORTANT: Manual Trust Step Required for OpenSSL Certificates ***");
        Console.WriteLine("Unlike mkcert, OpenSSL self-signed certificates require manual trust setup for browsers/OS.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("  For Windows (IIS/ASP.NET Core):");
            Console.WriteLine("  1. Open 'Manage Computer Certificates' (certlm.msc).");
            Console.WriteLine("  2. Navigate to 'Personal' -> 'Certificates'.");
            Console.WriteLine("  3. Right-click 'Certificates' -> All Tasks -> Import...");
            Console.WriteLine($"  4. Browse to '{Path.GetFullPath(pfxFileName)}', select it, and follow the wizard.");
            Console.WriteLine($"     (Password for this PFX is '{pfxExportPassword}')");
            Console.WriteLine("  5. After importing to Personal, you MUST also copy this certificate to 'Trusted Root Certification Authorities' -> 'Certificates'.");
            Console.WriteLine("     - Drag and drop the certificate from 'Personal' to 'Trusted Root Certification Authorities'.");
            Console.WriteLine("     - Or, right-click the certificate in 'Personal', select 'All Tasks' -> 'Export...', choose 'No, do not export the private key', save as .CER, then import that .CER into 'Trusted Root Certification Authorities'.");
            Console.WriteLine("  6. For IIS, you can then bind this certificate to your website. (This tool does NOT automate IIS binding for OpenSSL certificates, as the trust step is manual).");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine("  For macOS:");
            Console.WriteLine("  1. Double-click the generated '{pfxFileName}' file.");
            Console.WriteLine("  2. Keychain Access will open. Select 'login' or 'System' keychain.");
            Console.WriteLine($"  3. Enter the password '{pfxExportPassword}' when prompted.");
            Console.WriteLine("  4. Find the certificate, double-click it, expand 'Trust', and set 'When using this certificate:' to 'Always Trust'.");
            Console.WriteLine("  5. If using a web server like Nginx or Apache, you'll need to configure it to use the generated '{keyFileName}' and '{certFileName}'.");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("  For Linux:");
            Console.WriteLine("  You'll typically need to manually add the generated '{certFileName}' to your system's trusted certificate store.");
            Console.WriteLine("  Common locations/commands (may vary by distribution):");
            Console.WriteLine($"  - Copy '{certFileName}' to '/usr/local/share/ca-certificates/' (for Debian/Ubuntu) or '/etc/pki/ca-trust/source/anchors/' (for Fedora/RHEL).");
            Console.WriteLine("  - Run 'sudo update-ca-certificates' (Debian/Ubuntu) or 'sudo update-ca-trust extract' (Fedora/RHEL).");
            Console.WriteLine("  - For browsers like Firefox, you might need to import it directly through browser settings.");
            Console.WriteLine("  - For web servers (Nginx, Apache), configure them to use '{keyFileName}' and '{certFileName}'.");
        }

        return true;
    }

    // The DisplayHelp method (unchanged from previous update)
    public static void DisplayHelp()
    {
        Console.WriteLine(@"
================================================================================
||                            DEV CERTIFY HELP                                ||
================================================================================

DevCertify is a cross-platform .NET 9 CLI tool to simplify trusted SSL certs
for localhost and custom domain development.

--------------------------------------------------------------------------------
FEATURES
--------------------------------------------------------------------------------
* Cross-Platform Compatibility: Works on Windows, macOS, and Linux.

* Flexible Certificate Generation:
  - mkcert Method (Default):
    Automatically installs mkcert (via Chocolatey/Scoop on Windows, Homebrew
    on macOS) if not already present. Installs mkcert's local CA into your
    system's trust store, ensuring all generated certificates are automatically
    trusted by browsers.

  - OpenSSL Method (Optional):
    Generates certificates using OpenSSL, providing more control. This method
    requires OpenSSL to be installed manually and involves manual steps for
    establishing system/browser trust.

* localhost & Custom Domain Certificate Generation:
  Generates PKCS#12 (.p12 / .pfx) certificates for localhost or any specified
  custom domain(s) with Subject Alternative Names (SANs).

* Automated IIS Integration (Windows - mkcert only):
  For Windows users, when using the mkcert method, it automatically imports the
  generated certificate and configures the ""Default Web Site"" in IIS with an
  HTTPS binding.

* Clear Guidance:
  Provides clear instructions for manual steps required on macOS and Linux, or
  if automated configurations fail.

--------------------------------------------------------------------------------
INSTALLATION
--------------------------------------------------------------------------------
DevCertify is distributed as a .NET global tool.

1. Clone the Repository:
   git clone https://github.com/your-repo/devcertify.git
   cd devcertify

2. Pack the Tool:
   This command builds the project and creates a NuGet package (.nupkg) in the
   `nupkg` folder.
   dotnet pack

3. Install the Tool Globally:
   Install DevCertify using the local NuGet package.
   dotnet tool install --global DevCertify --add-source ./nupkg

   If you have a previous version installed, you can update it:
   dotnet tool update --global DevCertify --add-source ./nupkg

--------------------------------------------------------------------------------
USAGE
--------------------------------------------------------------------------------
To run DevCertify, you MUST open your terminal or command prompt with
administrator/sudo privileges. This is necessary for mkcert to install its
local CA into your system's trust store, for mkcert installation (if needed),
and for IIS configuration on Windows.

--- Using the `mkcert` Method (Recommended & Default) ---
This method is recommended for most users due to its automated trust setup.

* For `localhost`:
  devcertify

* For a custom domain (e.g., `myapp.local`):
  devcertify myapp.local
  (You might need to add `127.0.0.1 myapp.local` to your hosts file:
   `C:\Windows\System32\drivers\etc\hosts` on Windows, `/etc/hosts` on Linux/macOS)

The tool will:
1. Check for `mkcert` and install it if missing.
2. Run `mkcert -install` to set up a local Certificate Authority and add it to
   your system's trusted root store.
3. Generate a `.p12` (renamed to `.pfx`) certificate for the specified domain
   (or `localhost`) in the current directory.
4. Windows Only: Automatically import the `.pfx` and configure an HTTPS
   binding for the ""Default Web Site"" in IIS.
5. Provide instructions for manual steps on macOS/Linux or if IIS
   configuration fails.

--- Using the `OpenSSL` Method (Advanced) ---
This method provides more manual control but requires more manual steps for
trust establishment. OpenSSL must be installed manually beforehand.

* For `localhost`:
  devcertify --openssl

* For a custom domain (e.g., `api.dev`):
  devcertify --openssl api.dev

The tool will:
1. Check for `openssl` installation.
2. Generate a private key (`.key`), a Certificate Signing Request (`.csr`), a
   self-signed certificate (`.crt`), and a PKCS#12 (`.pfx`) file for the
   specified domain in the current directory.
3. Provide detailed manual instructions for:
   - Importing the `.pfx` into your system's personal certificate store.
   - Crucially, manually copying the certificate to your system's ""Trusted Root
     Certification Authorities"" store (or equivalent on macOS/Linux) to avoid
     browser warnings.
   - Configuring your web server (IIS, Nginx, Apache, etc.) to use the
     generated certificate.

--------------------------------------------------------------------------------
TROUBLESHOOTING
--------------------------------------------------------------------------------
* Permissions:
  Ensure you run the tool with administrator (Windows) or sudo (macOS/Linux)
  privileges. Most issues stem from insufficient permissions.

* `mkcert` Manual Install:
  If automatic `mkcert` installation fails, please follow the instructions on
  the official `mkcert` GitHub page:
  https://github.com/FiloSottile/mkcert
  to install it manually for your operating system.

* `OpenSSL` Not Found:
  If using the `--openssl` option, ensure OpenSSL is installed and accessible
  in your system's PATH.

* IIS Administration Module (Windows):
  If IIS configuration fails (with `mkcert`), ensure the `IISAdministration`
  PowerShell module is installed. You can install it via PowerShell:
  Install-Module IISAdministration -Scope AllUsers

* Browser Cache:
  After running the tool, clear your browser's cache or try an incognito/private
  window to ensure it picks up the new certificate.

* Web Server Restart:
  Restart your web server (e.g., IIS, Nginx, Apache) or application pool after
  the certificate setup. For IIS, you can run `iisreset` in an elevated command
  prompt.

* OpenSSL Trust:
  Remember that OpenSSL-generated certificates *require manual trust setup* in
  your OS and browsers. Follow the on-screen instructions carefully.

================================================================================
");
    }
}
