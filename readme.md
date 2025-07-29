# DevCertify

**DevCertify** is a cross-platform .NET 9 command-line interface (CLI) tool designed to simplify the creation and management of trusted SSL certificates for `localhost` and custom domain development environments. It offers two primary methods for certificate generation: leveraging `mkcert` for seamless, automatically trusted certificates, or using `OpenSSL` for more granular control with manual trust configuration. It also includes integrated setup for IIS on Windows.

## 🌟 Features

- **Cross-Platform Compatibility:** Works on Windows, macOS, and Linux.

- **Flexible Certificate Generation:**
  
  - **`mkcert` Method (Default):** Automatically installs `mkcert` (via Chocolatey/Scoop on Windows, Homebrew on macOS) if not already present. Installs `mkcert`'s local Certificate Authority (CA) into your system's trust store, ensuring all generated certificates are automatically trusted by browsers.
  
  - **`OpenSSL` Method (Optional):** Generates certificates using OpenSSL, providing more control over certificate properties. This method requires OpenSSL to be installed manually and involves manual steps for establishing system/browser trust.

- **`localhost` & Custom Domain Certificate Generation:** Generates PKCS#12 (`.p12` / `.pfx`) certificates for `localhost` or any specified custom domain(s) with Subject Alternative Names (SANs).

- **Automated IIS Integration (Windows - `mkcert` only):** For Windows users, when using the `mkcert` method, it automatically imports the generated certificate into the local machine's personal store and configures the "Default Web Site" in IIS with an HTTPS binding.

- **Clear Guidance:** Provides clear instructions for manual steps required on macOS and Linux, or if automated configurations fail.

## 🚀 Installation

**DevCertify** is distributed as a .NET global tool.

1. **Clone the Repository:**
   
   ```
   git clone https://github.com/your-repo/devcertify.git
   cd devcertify
   ```

2. Pack the Tool:
   
   This command builds the project and creates a NuGet package (.nupkg) in the nupkg folder.
   
   ```
   dotnet pack
   ```

3. Install the Tool Globally:
   
   Install DevCertify using the local NuGet package.
   
   ```
   dotnet tool install --global DevCertify --add-source ./nupkg
   ```
   
   If you have a previous version installed, you can update it:
   
   ```
   dotnet tool update --global DevCertify --add-source ./nupkg
   ```

## 💡 Usage

To run **DevCertify**, you **must open your terminal or command prompt with administrator/sudo privileges**. This is necessary for `mkcert` to install its local CA into your system's trust store, for `mkcert` installation (if needed), and for IIS configuration on Windows.

### Using the `mkcert` Method (Recommended & Default)

This method is recommended for most users due to its automated trust setup.

- **For `localhost`:**
  
  ```
  devcertify
  ```

- **For a custom domain (e.g., `myapp.local`):**
  
  ```
  devcertify myapp.local
  ```
  
  (You might need to add `127.0.0.1 myapp.local` to your hosts file: `C:\Windows\System32\drivers\etc\hosts` on Windows, `/etc/hosts` on Linux/macOS)

The tool will:

1. Check for `mkcert` and install it if missing.

2. Run `mkcert -install` to set up a local Certificate Authority and add it to your system's trusted root store.

3. Generate a `.p12` (renamed to `.pfx`) certificate for the specified domain (or `localhost`) in the current directory.

4. **Windows Only:** Automatically import the `.pfx` and configure an HTTPS binding for the "Default Web Site" in IIS.

5. Provide instructions for manual steps on macOS/Linux or if IIS configuration fails.

### Using the `OpenSSL` Method (Advanced)

This method provides more manual control but requires more manual steps for trust establishment. OpenSSL must be installed manually beforehand.

- **For `localhost`:**
  
  ```
  devcertify --openssl
  ```

- **For a custom domain (e.g., `api.dev`):**
  
  ```
  devcertify --openssl api.dev
  ```

The tool will:

1. Check for `openssl` installation.

2. Generate a private key (`.key`), a Certificate Signing Request (`.csr`), a self-signed certificate (`.crt`), and a PKCS#12 (`.pfx`) file for the specified domain in the current directory.

3. **Provide detailed manual instructions** for:
   
   - Importing the `.pfx` into your system's personal certificate store.
   
   - **Crucially, manually copying the certificate to your system's "Trusted Root Certification Authorities" store** (or equivalent on macOS/Linux) to avoid browser warnings.
   
   - Configuring your web server (IIS, Nginx, Apache, etc.) to use the generated certificate.

## ⚠️ Troubleshooting

- **Permissions:** Ensure you run the tool with administrator (Windows) or sudo (macOS/Linux) privileges. Most issues stem from insufficient permissions.

- **`mkcert` Manual Install:** If automatic `mkcert` installation fails, please follow the instructions on the [official `mkcert` GitHub page](https://www.google.com/search?q=%5Bhttps://github.com/FiloSottile/mkcert%5D(https://github.com/FiloSottile/mkcert) "null") to install it manually for your operating system.

- **`OpenSSL` Not Found:** If using the `--openssl` option, ensure OpenSSL is installed and accessible in your system's PATH.

- **IIS Administration Module (Windows):** If IIS configuration fails (with `mkcert`), ensure the `IISAdministration` PowerShell module is installed. You can install it via PowerShell:
  
  ```
  Install-Module IISAdministration -Scope AllUsers
  ```

- **Browser Cache:** After running the tool, clear your browser's cache or try an incognito/private window to ensure it picks up the new certificate.

- **Web Server Restart:** Restart your web server (e.g., IIS, Nginx, Apache) or application pool after the certificate setup. For IIS, you can run `iisreset` in an elevated command prompt.

- **OpenSSL Trust:** Remember that OpenSSL-generated certificates *require manual trust setup* in your OS and browsers. Follow the on-screen instructions carefully.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://www.google.com/search?q=LICENSE "null") file for details.