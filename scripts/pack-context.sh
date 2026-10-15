#!/usr/bin/env bash
set -euo pipefail

# Переходим в корень репозитория (на уровень выше папки scripts)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_FILE="${1:-ai_context.md}"
MODULE_FILTER="${2:-}"

echo "Сборка контекста проекта TypoErp в файл: $OUTPUT_FILE ..."

cat << 'EOF' > "$OUTPUT_FILE"
# КОНТЕКСТ ПРОЕКТА TYPOERP
Ниже представлены актуальные правила разработки, архитектура, доменная логика, текущий план (Roadmap), структура файлов и исходный код проекта.
Строго соблюдай все инструкции из раздела `00_AI_RULES.md`.

EOF

# 1. Добавляем все документационные файлы из docs/ai/
echo "## ЧАСТЬ 1: ПРАВИЛА, АРХИТЕКТУРА И СОСТОЯНИЕ ПРОЕКТА" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

if [ -d "docs/ai" ]; then
    for doc in $(find docs/ai -maxdepth 1 -type f -name "*.md" | sort); do
        echo "================================================================================" >> "$OUTPUT_FILE"
        echo "### ДОКУМЕНТ: \`$doc\`" >> "$OUTPUT_FILE"
        echo "================================================================================" >> "$OUTPUT_FILE"
        echo "" >> "$OUTPUT_FILE"
        cat "$doc" >> "$OUTPUT_FILE"
        echo -e "\n\n" >> "$OUTPUT_FILE"
    done
else
    echo "ВНИМАНИЕ: Папка docs/ai не найдена!" >> "$OUTPUT_FILE"
fi

# 2. Добавляем дерево файлов проекта
echo "## ЧАСТЬ 2: СТРУКТУРА ФАЙЛОВ ПРОЕКТА" >> "$OUTPUT_FILE"
echo '```text' >> "$OUTPUT_FILE"
find . -maxdepth 5 \
    -not -path '*/.*' \
    -not -path '*/bin*' \
    -not -path '*/obj*' \
    -not -path '*/TestResults*' \
    -not -name "$OUTPUT_FILE" \
    | sort >> "$OUTPUT_FILE"
echo '```' >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# 3. Добавляем исходный код проекта (корень solution, src и tests)
echo "## ЧАСТЬ 3: АКТУАЛЬНЫЙ ИСХОДНЫЙ КОД" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

while IFS= read -r file; do
    if [[ -n "$MODULE_FILTER" && "$file" != *"$MODULE_FILTER"* ]]; then
        continue
    fi

    ext="${file##*.}"
    lang="text"
    case "$ext" in
        cs) lang="csharp" ;;
        razor) lang="razor" ;;
        csproj|props|targets|slnx) lang="xml" ;;
        sln) lang="text" ;;
        json) lang="json" ;;
        css) lang="css" ;;
        js) lang="javascript" ;;
    esac

    echo "### Файл: \`$file\`" >> "$OUTPUT_FILE"
    echo "\`\`\`$lang" >> "$OUTPUT_FILE"
    cat "$file" >> "$OUTPUT_FILE"
    echo "" >> "$OUTPUT_FILE"
    echo "\`\`\`" >> "$OUTPUT_FILE"
    echo "" >> "$OUTPUT_FILE"
done < <(find . -maxdepth 6 -type f \
    \( -name "*.sln" -o -name "*.slnx" -o -name "Directory.Build.props" -o -path "./src/*" -o -path "./tests/*" \) \
    \( -name "*.cs" -o -name "*.razor" -o -name "*.csproj" -o -name "*.sln" -o -name "*.slnx" -o -name "appsettings*.json" -o -name "Directory.Build.props" \) \
    -not -path "*/bin/*" \
    -not -path "*/obj/*" \
    -not -path "*/.*" \
    | sort)

echo "Готово! Контекст успешно собран в $OUTPUT_FILE ($(wc -l < "$OUTPUT_FILE") строк)."