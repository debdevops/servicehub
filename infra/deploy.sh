#!/usr/bin/env bash
# ServiceHub — deploy to your own Azure, AWS or Google Cloud account (macOS and Linux).
# Windows: use deploy.ps1, which does the same thing.
#
#   ./deploy.sh <azure|aws|gcp>                 deploy (asks a few questions the first time)
#   ./deploy.sh <cloud> --doctor                only check that this computer is ready
#   ./deploy.sh <cloud> --plan                  show what would be created; change nothing
#   ./deploy.sh <cloud> --destroy               remove ServiceHub, KEEPING its data disk, backups and encryption key
#   ./deploy.sh <cloud> --destroy --purge       remove everything, data included (cannot be undone)
#   ./deploy.sh <cloud> --list                  list every resource carrying the ServiceHub tag
#
# Options:  --yes  --region R  --resource X (repeatable)  --topic T (gcp, repeatable)  --version V  --read-only
#           --subscription ID (azure)  --ssh-key FILE (azure)  --project ID (gcp)  --zone Z (gcp)
# The same values can be given as SERVICEHUB_REGION, SERVICEHUB_RESOURCES (comma-separated), SERVICEHUB_VERSION, …
#
# This script holds no knowledge of any cloud's resources: Terraform creates and removes everything (see infra/<cloud>/).
# Exit codes: 0 done · 1 failed · 2 this computer is not ready · 3 you said no.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CLOUD="${1:-}"
[ $# -gt 0 ] && shift

ACTION=deploy
PURGE=0
YES=0
READ_ONLY="${SERVICEHUB_READ_ONLY:-0}"
REGION="${SERVICEHUB_REGION:-}"
VERSION="${SERVICEHUB_VERSION:-}"
RESOURCES="${SERVICEHUB_RESOURCES:-}"
TOPICS="${SERVICEHUB_TOPICS:-}"
SUBSCRIPTION="${SERVICEHUB_SUBSCRIPTION:-}"
SSH_KEY_FILE="${SERVICEHUB_SSH_KEY:-}"
PROJECT="${SERVICEHUB_PROJECT:-}"
ZONE="${SERVICEHUB_ZONE:-}"

say()  { printf '%s\n' "$*"; }
warn() { printf 'Note: %s\n' "$*" >&2; }
die()  { printf 'Stopped: %s\n' "$1" >&2; exit "${2:-1}"; }

usage() { sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --doctor) ACTION=doctor ;;
    --plan) ACTION=plan ;;
    --destroy) ACTION=destroy ;;
    --list) ACTION=list ;;
    --purge) PURGE=1 ;;
    --yes|-y) YES=1 ;;
    --read-only) READ_ONLY=1 ;;
    --region) REGION="${2:-}"; shift ;;
    --version) VERSION="${2:-}"; shift ;;
    --resource) RESOURCES="${RESOURCES:+$RESOURCES,}${2:-}"; shift ;;
    --topic) TOPICS="${TOPICS:+$TOPICS,}${2:-}"; shift ;;
    --subscription) SUBSCRIPTION="${2:-}"; shift ;;
    --ssh-key) SSH_KEY_FILE="${2:-}"; shift ;;
    --project) PROJECT="${2:-}"; shift ;;
    --zone) ZONE="${2:-}"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "I do not know the option '$1'. Run ./deploy.sh --help" 2 ;;
  esac
  shift
done

case "$CLOUD" in
  azure|aws|gcp) ;;
  -h|--help|"") usage; exit 0 ;;
  *) die "Say which cloud: azure, aws or gcp." 2 ;;
esac
[ "$PURGE" = 1 ] && [ "$ACTION" != destroy ] && die "--purge only goes with --destroy." 2

DIR="$HERE/$CLOUD"
TFVARS="$DIR/terraform.tfvars"
tf() { terraform -chdir="$DIR" "$@"; }

ask() { # ask "question" "default"
  local answer=""
  if [ "$YES" = 1 ]; then printf '%s' "$2"; return; fi
  if [ -n "$2" ]; then printf '%s [%s]: ' "$1" "$2" >&2; else printf '%s: ' "$1" >&2; fi
  read -r answer || true
  printf '%s' "${answer:-$2}"
}

confirm() { # confirm "question" -> returns 0 for yes
  local answer=""
  [ "$YES" = 1 ] && return 0
  printf '%s [y/N]: ' "$1" >&2
  read -r answer || true
  case "$answer" in y|Y|yes|YES) return 0 ;; *) return 1 ;; esac
}

# "a, b" -> ["a", "b"]
hcl_list() {
  local out="" item rest="$1"
  while [ -n "$rest" ]; do
    item="${rest%%,*}"
    if [ "$item" = "$rest" ]; then rest=""; else rest="${rest#*,}"; fi
    item="$(printf '%s' "$item" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')"
    [ -n "$item" ] && out="${out:+$out, }\"$item\""
  done
  printf '[%s]' "$out"
}

# ── 1. Is this computer ready? ───────────────────────────────────────────────────────────────────
doctor() {
  local ok=1 account=""
  if ! command -v terraform >/dev/null 2>&1; then
    say "✗ Terraform is not installed.   https://developer.hashicorp.com/terraform/install"; ok=0
  else
    say "✓ Terraform $(terraform version | head -1 | sed 's/^Terraform //')"
  fi
  case "$CLOUD" in
    azure)
      if ! command -v az >/dev/null 2>&1; then say "✗ The Azure CLI is not installed.   https://learn.microsoft.com/cli/azure/install-azure-cli"; ok=0
      elif ! account="$(az account show --query '[name,id]' -o tsv 2>/dev/null | tr '\n\t' '  ')"; then say "✗ Not signed in to Azure.   Run: az login"; ok=0
      else say "✓ Azure subscription: $account"; fi ;;
    aws)
      if ! command -v aws >/dev/null 2>&1; then say "✗ The AWS CLI is not installed.   https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html"; ok=0
      elif ! account="$(aws sts get-caller-identity --query '[Account,Arn]' --output text 2>/dev/null | tr '\t' ' ')"; then say "✗ Not signed in to AWS.   Run: aws configure   (or: aws sso login)"; ok=0
      else say "✓ AWS account: $account"; fi
      command -v session-manager-plugin >/dev/null 2>&1 || warn "the Session Manager plugin is not installed. You need it to open ServiceHub afterwards: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html" ;;
    gcp)
      if ! command -v gcloud >/dev/null 2>&1; then say "✗ The Google Cloud CLI is not installed.   https://cloud.google.com/sdk/docs/install"; ok=0
      elif ! account="$(gcloud auth list --filter=status:ACTIVE --format='value(account)' 2>/dev/null | head -1)" || [ -z "$account" ]; then say "✗ Not signed in to Google Cloud.   Run: gcloud auth login   and   gcloud auth application-default login"; ok=0
      else
        say "✓ Google account: $account"
        gcloud auth application-default print-access-token >/dev/null 2>&1 || { say "✗ Terraform has no Google credentials.   Run: gcloud auth application-default login"; ok=0; }
      fi ;;
  esac
  [ "$ok" = 1 ] || die "This computer is not ready yet — fix the lines marked ✗ and run it again." 2
}

# ── 2. The answers, written once to terraform.tfvars (names only — never a secret) ───────────────
configure() {
  if [ -f "$TFVARS" ]; then
    say "Using the answers already in $CLOUD/terraform.tfvars (edit or delete that file to change them)."
    return
  fi
  local resources_hint
  {
    printf '# Written by deploy.sh. Names only — no secret belongs in this file.\n'
    case "$CLOUD" in
      azure)
        [ -n "$SUBSCRIPTION" ] || SUBSCRIPTION="$(ask 'Azure subscription ID' "$(az account show --query id -o tsv 2>/dev/null || true)")"
        [ -n "$REGION" ] || REGION="$(ask 'Azure region (for example westeurope)' '')"
        if [ -z "$SSH_KEY_FILE" ]; then
          for candidate in "$HOME/.ssh/id_ed25519.pub" "$HOME/.ssh/id_rsa.pub"; do [ -f "$candidate" ] && { SSH_KEY_FILE="$candidate"; break; }; done
          SSH_KEY_FILE="$(ask 'Your SSH PUBLIC key file' "$SSH_KEY_FILE")"
        fi
        [ -f "$SSH_KEY_FILE" ] || die "There is no file '$SSH_KEY_FILE'. Make a key with: ssh-keygen -t ed25519" 2
        case "$SSH_KEY_FILE" in *.pub) ;; *) die "'$SSH_KEY_FILE' does not look like a PUBLIC key (it should end in .pub)." 2 ;; esac
        MY_IP="$(ask 'Your own public IP address — the only one allowed to open the tunnel (see https://api.ipify.org)' "${SERVICEHUB_MY_IP:-}")"
        [ -n "$SUBSCRIPTION" ] && [ -n "$REGION" ] && [ -n "$MY_IP" ] || die "The subscription, the region and your IP address are all needed." 2
        printf 'subscription_id  = "%s"\nregion           = "%s"\nssh_public_key   = "%s"\nallowed_ssh_cidr = "%s/32"\n' "$SUBSCRIPTION" "$REGION" "$(tr -d '\n' <"$SSH_KEY_FILE")" "${MY_IP%/32}"
        resources_hint='Service Bus namespace resource IDs to note on the VM, comma-separated (optional)' ;;
      aws)
        [ -n "$REGION" ] || REGION="$(ask 'AWS region (for example eu-west-1)' "$(aws configure get region 2>/dev/null || true)")"
        [ -n "$REGION" ] || die "The region is needed." 2
        printf 'region = "%s"\n' "$REGION"
        resources_hint='ARNs of the queues ServiceHub may use — each queue AND its dead-letter queue, comma-separated' ;;
      gcp)
        [ -n "$PROJECT" ] || PROJECT="$(ask 'Google Cloud project ID' "$(gcloud config get-value project 2>/dev/null || true)")"
        [ -n "$REGION" ] || REGION="$(ask 'Region (for example europe-west1)' '')"
        [ -n "$ZONE" ] || ZONE="$(ask 'Zone' "${REGION:+$REGION-b}")"
        [ -n "$PROJECT" ] && [ -n "$REGION" ] && [ -n "$ZONE" ] || die "The project, the region and the zone are all needed." 2
        printf 'project_id = "%s"\nregion     = "%s"\nzone       = "%s"\n' "$PROJECT" "$REGION" "$ZONE"
        [ -n "$TOPICS" ] || TOPICS="$(ask 'Topics a replayed message is published back to, comma-separated (optional)' '')"
        printf 'replay_topics = %s\n' "$(hcl_list "$TOPICS")"
        resources_hint='Subscriptions ServiceHub may read — each subscription AND its dead-letter subscription, comma-separated' ;;
    esac
    [ -n "$RESOURCES" ] || RESOURCES="$(ask "$resources_hint" '')"
    printf 'messaging_resources = %s\n' "$(hcl_list "$RESOURCES")"
    [ "$READ_ONLY" = 1 ] && printf 'allow_replay = false\n'
    [ -n "$VERSION" ] && printf 'servicehub_version = "%s"\n' "$VERSION"
    true
  } >"$TFVARS.new"
  mv "$TFVARS.new" "$TFVARS"
  say "Wrote $CLOUD/terraform.tfvars"
}

# One output value as plain text; a list becomes one item per line.
output_lines() { tf output -json "$1" 2>/dev/null | tr -d '[]"' | tr ',' '\n' | sed 's/^[[:space:]]*//; s/[[:space:]]*$//' | sed '/^$/d'; }

list_tagged() {
  local command
  command="$(tf output -raw list_command 2>/dev/null || true)"
  [ -n "${1:-}" ] && command="$1"
  [ -n "$command" ] || { say "Nothing is deployed from this folder, so there is no account to look in."; return; }
  say "Everything carrying the ServiceHub tag:"
  sh -c "$command" || warn "the cloud's CLI could not list resources — run it yourself: $command"
}

# ── 3. Actions ───────────────────────────────────────────────────────────────────────────────────
doctor
[ "$ACTION" = doctor ] && { say "This computer is ready."; exit 0; }

if [ "$ACTION" = list ]; then
  tf init -input=false >/dev/null
  list_tagged
  exit 0
fi

if [ "$ACTION" = destroy ]; then
  [ -f "$TFVARS" ] || die "There is no $CLOUD/terraform.tfvars, so nothing was deployed from this folder." 1
  tf init -input=false >/dev/null
  LIST_COMMAND="$(tf output -raw list_command 2>/dev/null || true)"
  if [ "$PURGE" = 1 ]; then
    say "This removes EVERYTHING, including ServiceHub's data, its backups and its encryption key. It cannot be undone."
    if [ "$YES" != 1 ]; then
      printf 'Type PURGE to go on: ' >&2
      read -r typed || true
      [ "$typed" = PURGE ] || die "Nothing was removed." 3
    fi
  else
    say "This removes ServiceHub but KEEPS:"
    output_lines kept_on_destroy | sed 's/^/  - /'
    say "Run it again with --purge to remove those too."
    confirm "Remove ServiceHub from $CLOUD?" || die "Nothing was removed." 3
    # What is kept is taken out of Terraform's hands, so the destroy below cannot touch it.
    for address in $(output_lines kept_addresses); do
      tf state rm "$address" >/dev/null 2>&1 || true
    done
  fi
  tf destroy -input=false -auto-approve || die "The destroy did not finish. It is safe to run the same command again." 1
  if [ "$PURGE" = 1 ]; then
    # Snapshots are made by the cloud's own schedule, not by Terraform, so they are removed here.
    case "$CLOUD" in
      aws) for snap in $(aws ec2 describe-snapshots --owner-ids self --region "$(sed -n 's/^region *= *"\(.*\)"/\1/p' "$TFVARS")" --filters Name=tag:app,Values=servicehub --query 'Snapshots[].SnapshotId' --output text 2>/dev/null); do aws ec2 delete-snapshot --region "$(sed -n 's/^region *= *"\(.*\)"/\1/p' "$TFVARS")" --snapshot-id "$snap" || true; done ;;
      gcp) for snap in $(gcloud compute snapshots list --project "$(sed -n 's/^project_id *= *"\(.*\)"/\1/p' "$TFVARS")" --filter='labels.app=servicehub' --format='value(name)' 2>/dev/null); do gcloud compute snapshots delete "$snap" --project "$(sed -n 's/^project_id *= *"\(.*\)"/\1/p' "$TFVARS")" --quiet || true; done ;;
    esac
    rm -f "$TFVARS"
  fi
  say ""
  list_tagged "$LIST_COMMAND"
  [ "$PURGE" = 1 ] && say "If the list above is empty, nothing of ServiceHub is left."
  exit 0
fi

configure
tf init -input=false >/dev/null || die "Terraform could not start in infra/$CLOUD." 1
tf plan -input=false -out=servicehub.tfplan || die "Terraform could not work out a plan — the message above says why." 1
[ "$ACTION" = plan ] && { rm -f "$DIR/servicehub.tfplan"; say "Nothing was changed."; exit 0; }

say ""
say "The plan above lists everything that will be created. What it costs is in infra/$CLOUD/README.md."
confirm "Create ServiceHub in $CLOUD now?" || { rm -f "$DIR/servicehub.tfplan"; die "Nothing was created." 3; }
if ! tf apply -input=false servicehub.tfplan; then
  rm -f "$DIR/servicehub.tfplan"
  die "It did not finish. Run the same command again to carry on, or add --destroy to remove what was made." 1
fi
rm -f "$DIR/servicehub.tfplan"
say ""
tf output -raw next_steps
