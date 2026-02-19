#!/usr/bin/env bash
set -euo pipefail

# Downloads the local embedding model into:
#   models/bge-base-en-v1.5
# Run from project root:
#   bash ./download-model.sh

ROOT_DIR="$(pwd)"
TARGET_DIR="$ROOT_DIR/models/bge-base-en-v1.5"
BASE_URL="https://huggingface.co/BAAI/bge-base-en-v1.5/resolve/main"

if [[ ! -f "$ROOT_DIR/docker-compose.yml" || ! -d "$ROOT_DIR/dotnet" ]]; then
  echo "Error: run this script from the project root (where docker-compose.yml exists)." >&2
  exit 1
fi

mkdir -p "$TARGET_DIR/1_Pooling"
mkdir -p "$TARGET_DIR/onnx"

download_file() {
  local relative_path="$1"
  local output_path="$TARGET_DIR/$relative_path"
  local url="$BASE_URL/$relative_path"

  echo "Downloading $relative_path"
  curl -fL --retry 5 --retry-delay 2 --retry-all-errors \
    -o "$output_path" "$url"
}

FILES=(
  ".gitattributes"
  "config_sentence_transformers.json"
  "config.json"
  "model.safetensors"
  "modules.json"
  "README.md"
  "sentence_bert_config.json"
  "special_tokens_map.json"
  "tokenizer_config.json"
  "tokenizer.json"
  "vocab.txt"
  "1_Pooling/config.json"
  "onnx/model.onnx"
)

for file in "${FILES[@]}"; do
  download_file "$file"
done

echo

echo "Model downloaded to: $TARGET_DIR"
echo "Done."
