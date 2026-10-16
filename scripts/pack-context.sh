#!/usr/bin/env bash
set -euo pipefail

# Переходим в корень репозитория (на уровень выше папки scripts)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

RAW_NAME="${1:-ai_context}"
BASE_NAME="${RAW_NAME%.md}"
MODULE_FILTER="${2:-}"
MAX_LINES="${3:-2500}"

rm -f "${BASE_NAME}.md" "${BASE_NAME}_part"*.md

PART=1
CURRENT_FILE="${BASE_NAME}_part${PART}.md"
CURRENT_LINES=0

TMP_FILES="$(mktemp)"
trap 'rm -f "$TMP_FILES"' EXIT

start_new_part() {
    local prev_file="$CURRENT_FILE"
    echo "" >> "$prev_file"
    echo "================================================================================" >> "$prev_file"
    echo "⏳ КОНЕЦ ЧАСТИ ${PART}. НЕ ОТВЕЧАЙ НА ЭТО СООБЩЕНИЕ — ЖДИ ЗАГРУЗКИ ЧАСТИ $((PART + 1))." >> "$prev_file"
    echo "================================================================================" >> "$prev_file"

    PART=$((PART + 1))
    CURRENT_FILE="${BASE_NAME}_part${PART}.md"

    cat << EOF > "$CURRENT_FILE"
# КОНТЕКСТ ПРОЕКТА TYPOERP (ЧАСТЬ ${PART} — ПРОДОЛЖЕНИЕ ИСХОДНОГО КОДА)
Это продолжение контекста проекта TypoErp. Строго соблюдай правила из Части 1 (\`00_AI_RULES.md\`).

EOF
    CURRENT_LINES=$(wc -l < "$CURRENT_FILE")
}

echo "Сборка контекста проекта TypoErp (лимит чанка: ~${MAX_LINES} строк)..."

cat << 'EOF' > "$CURRENT_FILE"
# КОНТЕКСТ ПРОЕКТА TYPOERP (ЧАСТЬ 1)
Ниже представлены актуальные правила разработки, архитектура, доменная логика, текущий план (Roadmap), структура файлов и исходный код проекта.
Строго соблюдай все инструкции из раздела `00_AI_RULES.md`.

EOF

# 1. Добавляем все документационные файлы из docs/ai/
echo "## ЧАСТЬ 1: ПРАВИЛА, АРХИТЕКТУРА И СОСТОЯНИЕ ПРОЕКТА" >> "$CURRENT_FILE"
echo "" >> "$CURRENT_FILE"

if [ -d "docs/ai" ]; then
    find docs/ai -maxdepth 2 -type f -name "*.md" | sort > "$TMP_FILES"
    while IFS= read -r doc; do
        echo "================================================================================" >> "$CURRENT_FILE"
        echo "### ДОКУМЕНТ: \`$doc\`" >> "$CURRENT_FILE"
        echo "================================================================================" >> "$CURRENT_FILE"
        echo "" >> "$CURRENT_FILE"
        cat "$doc" >> "$CURRENT_FILE"
        echo -e "\n\n" >> "$CURRENT_FILE"
    done < "$TMP_FILES"
else
    echo "ВНИМАНИЕ: Папка docs/ai не найдена!" >> "$CURRENT_FILE"
fi

# 2. Добавляем дерево файлов проекта (в одну строку без переносов \)
echo "## ЧАСТЬ 2: СТРУКТУРА ФАЙЛОВ ПРОЕКТА" >> "$CURRENT_FILE"
echo '```text' >> "$CURRENT_FILE"
find . -maxdepth 12 \( -path "*/.*" -o -name "bin" -o -name "obj" -o -name "TestResults" -o -name "node_modules" -o -path "*/wwwroot/lib" \) -prune -o -not -name "${BASE_NAME}*.md" -print | sort >> "$CURRENT_FILE"
echo '```' >> "$CURRENT_FILE"
echo "" >> "$CURRENT_FILE"

# 3. Добавляем исходный код проекта с разбивкой на чанки
echo "## ЧАСТЬ 3: АКТУАЛЬНЫЙ ИСХОДНЫЙ КОД" >> "$CURRENT_FILE"
echo "" >> "$CURRENT_FILE"

CURRENT_LINES=$(wc -l < "$CURRENT_FILE")

find . -maxdepth 15 \( -path "*/.*" -o -name "bin" -o -name "obj" -o -name "TestResults" -o -name "node_modules" -o -path "*/wwwroot/lib" \) -prune -o -type f -print | sort > "$TMP_FILES"

while IFS= read -r file; do
    # Пропускаем автогенерируемые файлы миграций и сборок
    case "$file" in
        *.Designer.cs|*ModelSnapshot.cs|*.g.cs) continue ;;
    esac

    # Берем файлы из src/, tests/ и ключевые конфиги из корня
    case "$file" in
        ./src/*|./tests/*)
            if [ -n "$MODULE_FILTER" ]; then
                case "$file" in
                    *"$MODULE_FILTER"*) ;;
                    *) continue ;;
                esac
            fi
            ;;
        ./*.sln|./*.slnx|./Directory.Build.props|./Directory.Packages.props|./global.json|./Dockerfile*|./docker-compose*.yml)
            ;;
        *)
            continue
            ;;
    esac

    # Определяем язык подсветки и отсекаем ненужные расширения
    lang=""
    case "$file" in
        */Dockerfile*) lang="dockerfile" ;;
        */appsettings*.json|./global.json) lang="json" ;;
        *.cs) lang="csharp" ;;
        *.razor) lang="razor" ;;
        *.csproj|*.props|*.targets|*.slnx) lang="xml" ;;
        *.sln) lang="text" ;;
        *.css) lang="css" ;;
        *.js) lang="javascript" ;;
        *.sql) lang="sql" ;;
        *.yml|*.yaml) lang="yaml" ;;
        *) continue ;;
    esac

    FILE_LINES=$(wc -l < "$file")
    NEXT_LINES=$((CURRENT_LINES + FILE_LINES + 6))

    if [ "$NEXT_LINES" -gt "$MAX_LINES" ] && [ "$CURRENT_LINES" -gt 100 ]; then
        start_new_part
    fi

    {
        echo "### Файл: \`$file\`"
        echo "\`\`\`$lang"
        cat "$file"
        echo ""
        echo "\`\`\`"
        echo ""
    } >> "$CURRENT_FILE"

    CURRENT_LINES=$((CURRENT_LINES + FILE_LINES + 5))

done < "$TMP_FILES"

echo "================================================================================" >> "$CURRENT_FILE"
echo "✅ КОНЕЦ КОНТЕКСТА (ЧАСТЬ ${PART} ИЗ ${PART}). Весь проект загружен." >> "$CURRENT_FILE"
echo "================================================================================" >> "$CURRENT_FILE"

if [ "$PART" -eq 1 ]; then
    mv "$CURRENT_FILE" "${BASE_NAME}.md"
    echo "Готово! Весь проект поместился в один файл: ${BASE_NAME}.md ($(wc -l < "${BASE_NAME}.md") строк)."
else
    echo "Готово! Контекст разбит на ${PART} частей:"
    for p in $(seq 1 "$PART"); do
        f="${BASE_NAME}_part${p}.md"
        echo "  - $f ($(wc -l < "$f") строк)"
    done
fi