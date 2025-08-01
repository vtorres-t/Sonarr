#!/bin/bash
### Description: Sonarr .NET Alpine install
### Version v1.0.0 2025-07-30 - vtorres - Initial


### Boilerplate Warning
#THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
#EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
#MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
#NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
#LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
#OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
#WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

scriptversion="1.0.0"
scriptdate="2025-07-30"

set -euo pipefail

echo "Running Sonarr Install Script - Version [$scriptversion] as of [$scriptdate]"

# Am I root?, need root!

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root."
    exit
fi

app="sonarr"
app_port="8989"
app_prereq="curl sqlite3 wget"
app_umask="0002"
branch="main"

# Constants
### Update these variables as required for your specific instance
installdir="/opt"              # {Update me if needed} Install Location
bindir="${installdir}/${app^}" # Full Path to Install Location
datadir="/var/lib/$app/"       # {Update me if needed} AppData directory to use
app_bin=${app^}                # Binary Name of the app

# This script should not be ran from installdir, otherwise later in the script the extracted files will be removed before they can be moved to installdir.
if [ "$installdir" == "$(dirname -- "$( readlink -f -- "$0"; )")" ] || [ "$bindir" == "$(dirname -- "$( readlink -f -- "$0"; )")" ]; then
    echo "You should not run this script from the intended install directory. The script will exit. Please re-run it from another directory"
    exit
fi

show_help() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Options:
  --user <name>       What user will $app run under?
                      User will be created if it doesn't already exist.

  --group <name>      What group will $app run under?
                      Group will be created if it doesn't already exist.

  -u                  Unattended mode
                      The installer will not prompt or pause, making it suitable for automated installations.
                      This option requires the use of --user and --group to supply those inputs for the script.

  -h, --help          Show this help message and exit
EOF
}

# Default values for command-line arguments
arg_user=""
arg_group=""
arg_unattended=false

# Parse command-line arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --user=*)
            arg_user="${1#*=}"
            shift
            ;;
        --user)
            if [[ -n "$2" && "$2" != -* ]]; then
                arg_user="$2"
                shift 2
            else
                echo "Error: --user requires a value." >&2
                exit 1
            fi
            ;;
        --group=*)
            arg_group="${1#*=}"
            shift
            ;;
        --group)
            if [[ -n "$2" && "$2" != -* ]]; then
                arg_group="$2"
                shift 2
            else
                echo "Error: --group requires a value." >&2
                exit 1
            fi
            ;;
        -u)
            arg_unattended=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Use --help to see valid options." >&2
            exit 1
            ;;
    esac
done

# If unattended mode is requested, require user and group
if $arg_unattended; then
    if [[ -z "$arg_user" || -z "$arg_group" ]]; then
        echo "Error: --user and --group are required when using -u (unattended mode)." >&2
        exit 1
    fi
fi

# Prompt User if necessary
if [ -n "$arg_user" ]; then
    app_uid="$arg_user"
else
    read -r -p "What user should ${app^} run as? (Default: $app): " app_uid < /dev/tty
fi
app_uid=$(echo "$app_uid" | tr -d ' ')
app_uid=${app_uid:-$app}

# Prompt Group if necessary
if [ -n "$arg_group" ]; then
    app_guid="$arg_group"
else
    read -r -p "What group should ${app^} run as? (Default: media): " app_guid < /dev/tty
fi
app_guid=$(echo "$app_guid" | tr -d ' ')
app_guid=${app_guid:-media}

echo "This will install [${app^}] to [$bindir] and use [$datadir] for the AppData Directory"
echo "${app^} will run as the user [$app_uid] and group [$app_guid]. By continuing, you've confirmed that the selected user and group will have READ and WRITE access to your Media Library and Download Client Completed Download directories"
if ! $arg_unattended; then
    read -n 1 -r -s -p $'Press enter to continue or ctrl+c to exit...\n' < /dev/tty
fi

# Create User / Group as needed
if [ "$app_guid" != "$app_uid" ]; then
    if ! getent group "$app_guid" >/dev/null; then
        groupadd "$app_guid"
    fi
fi
if ! getent passwd "$app_uid" >/dev/null; then
    adduser --system --no-create-home --ingroup "$app_guid" "$app_uid"
    echo "Created and added User [$app_uid] to Group [$app_guid]"
fi
if ! getent group "$app_guid" | grep -qw "$app_uid"; then
    echo "User [$app_uid] did not exist in Group [$app_guid]"
    usermod -a -G "$app_guid" "$app_uid"
    echo "Added User [$app_uid] to Group [$app_guid]"
fi

# Stop the App if running
if service --status-all | grep -Fq "$app"; then
    systemctl stop "$app"
    systemctl disable "$app".service
    echo "Stopped existing $app"
fi

# Create Appdata Directory

# AppData
mkdir -p "$datadir"
chown -R "$app_uid":"$app_guid" "$datadir"
chmod 775 "$datadir"
echo "Directories created"
# Download and install the App

# prerequisite packages
echo ""
echo "Installing pre-requisite Packages"
# shellcheck disable=SC2086
apt update && apt install -y $app_prereq
echo ""
ARCH=$(dpkg --print-architecture)
# get arch
dlbase="https://services.sonarr.tv/v1/download/$branch/latest?version=4&os=linux"
case "$ARCH" in
"amd64") DLURL="${dlbase}&arch=x64" ;;
"armhf") DLURL="${dlbase}&arch=arm" ;;
"arm64") DLURL="${dlbase}&arch=arm64" ;;
*)
    echo "Arch not supported"
    exit 1
    ;;
esac
echo ""
echo "Removing previous tarballs"
# -f to Force so we fail if it doesn't exist
rm -f "${app^}".*.tar.gz
echo ""
echo "Downloading..."
wget --content-disposition "$DLURL"
tar -xvzf "${app^}".*.tar.gz
echo ""
echo "Installation files downloaded and extracted"

# remove existing installs
echo "Removing existing installation"
rm -rf "$bindir"
echo "Installing..."
mv "${app^}" $installdir
chown "$app_uid":"$app_guid" -R "$bindir"
chmod 775 "$bindir"
rm -rf "${app^}.*.tar.gz"
# Ensure we check for an update in case user installs older version or different branch
touch "$datadir"/update_required
chown "$app_uid":"$app_guid" "$datadir"/update_required
echo "App Installed"
# Configure Autostart

# Remove any previous app .service
echo "Removing old service file"
rm -rf /etc/systemd/system/"$app".service

# Create app .service with correct user startup
echo "Creating service file"
cat <<EOF | tee /etc/systemd/system/"$app".service >/dev/null
[Unit]
Description=${app^} Daemon
After=syslog.target network.target
[Service]
User=$app_uid
Group=$app_guid
UMask=$app_umask
Type=simple
ExecStart=$bindir/$app_bin -nobrowser -data=$datadir
TimeoutStopSec=20
KillMode=process
Restart=on-failure
[Install]
WantedBy=multi-user.target
EOF

# Start the App
echo "Service file created. Attempting to start the app"
systemctl -q daemon-reload
systemctl enable --now -q "$app"

# Finish Update/Installation
host=$(hostname -I)
ip_local=$(grep -oP '^\S*' <<<"$host")
echo ""
echo "Install complete"
sleep 10
STATUS="$(systemctl is-active "$app")"
if [ "${STATUS}" = "active" ]; then
    echo "Browse to http://$ip_local:$app_port for the ${app^} GUI"
else
    echo "${app^} failed to start"
fi

# Exit
exit 0
