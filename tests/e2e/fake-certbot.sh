#!/bin/sh
# A stand-in for certbot, used by the end to end tests only. It cannot talk to Let's Encrypt, but it keeps certbot's command
# line and its on-disk layout (live/<name>/fullchain.pem and privkey.pem), which is everything Roastgate relies on.
command="$1"; shift
config=/etc/letsencrypt
name=""; domains=""; dry=""; force=""
while [ $# -gt 0 ]; do
  case "$1" in
    --cert-name) name="$2"; shift ;;
    --config-dir) config="$2"; shift ;;
    -d) domains="$domains $2"; shift ;;
    --dry-run) dry=1 ;;
    --force-renewal) force=1 ;;
  esac
  shift
done

# make_cert <name> <domains> <days>: a real, self-signed X.509 certificate in certbot's layout.
make_cert() {
  dir="$config/live/$1"; mkdir -p "$dir"
  san=""; first=""
  for d in $2; do
    [ -z "$first" ] && first="$d"
    san="$san${san:+,}DNS:$d"
  done
  openssl req -x509 -nodes -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 \
    -keyout "$dir/privkey.pem" -out "$dir/fullchain.pem" -subj "/CN=$first" -addext "subjectAltName=$san" -days "$3" >/dev/null 2>&1
}

case "$command" in
  certonly)
    if [ -n "$dry" ]; then echo "The dry run was successful."; exit 0; fi
    make_cert "$name" "$domains" 90
    echo "Successfully received certificate."
    ;;
  renew)
    for dir in "$config"/live/*/; do
      [ -f "$dir/fullchain.pem" ] || continue
      current=$(basename "$dir")
      [ -n "$name" ] && [ "$name" != "$current" ] && continue
      # Like certbot: only what expires within 30 days is renewed, unless forced.
      if [ -n "$force" ] || ! openssl x509 -checkend 2592000 -noout -in "$dir/fullchain.pem" >/dev/null; then
        names=$(openssl x509 -noout -ext subjectAltName -in "$dir/fullchain.pem" | tr -d ' ' | grep -o 'DNS:[^,]*' | sed 's/DNS://' | tr '\n' ' ')
        make_cert "$current" "$names" 90
        echo "Congratulations, all renewals succeeded: $current"
      fi
    done
    ;;
  delete)
    rm -rf "$config/live/$name"
    echo "Deleted all files relating to certificate $name."
    ;;
  *)
    echo "unsupported command: $command"
    exit 2
    ;;
esac
