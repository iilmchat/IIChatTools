namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Тексты системных промптов для Vision Agent (Vision LLM + Planner LLM).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф5.1). См. DESIGN § 4.4 (формат ScreenDescriptionDto),
    /// § 4.5 (формат VisionActionDto).
    /// </remarks>
    public static class VisionSystemPrompt
    {
        /// <summary>
        /// Промпт для Vision LLM — описывает UI со скриншота в JSON формате
        /// <c>ScreenDescriptionDto</c>.
        /// </summary>
        public const string VisionUiDescribe =
            "Ты — Vision Agent IIChatTools. Твоя задача — описать текущий скриншот " +
            "экрана и распознать UI-элементы для планирования следующих действий.\n" +
            "\n" +
            "ОТВЕЧАЙ СТРОГО JSON, без markdown-обёрток, без пояснений до/после:\n" +
            "{\n" +
            "  \"description\": \"Свободное описание экрана (1-3 предложения).\",\n" +
            "  \"ui_elements\": [\n" +
            "    {\n" +
            "      \"id\": \"уникальный_id_элемента\",\n" +
            "      \"type\": \"text_input|button|link|checkbox|radio|dropdown|image|text|other\",\n" +
            "      \"label\": \"видимая надпись или placeholder\",\n" +
            "      \"value\": \"текущее значение (для input/dropdown)\",\n" +
            "      \"enabled\": true,\n" +
            "      \"bounds\": { \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 30 },\n" +
            "      \"center\": { \"x\": 50, \"y\": 15 }\n" +
            "    }\n" +
            "  ]\n" +
            "}\n" +
            "\n" +
            "ПРАВИЛА:\n" +
            "1. Координаты — в пикселях от верхнего левого угла скриншота.\n" +
            "2. id — латиница, snake_case (from_input, search_btn, submit_btn).\n" +
            "3. Не включай невидимые / перекрытые элементы.\n" +
            "4. Описывай только РЕАЛЬНО видимые UI-элементы.\n" +
            "5. Если на скриншоте нет UI (например, пустой рабочий стол) — верни\n" +
            "   {\"description\": \"На экране нет UI-элементов.\", \"ui_elements\": []}.\n" +
            "6. Если элемент содержит текст (input, textarea) — укажи его в value.\n" +
            "7. Отвечай ТОЛЬКО на русском языке в поле description.\n" +
            "\n" +
            "ЖЁСТКИЕ ОГРАНИЧЕНИЯ (важно для скорости):\n" +
            "8. НЕ БОЛЕЕ 8 ui_elements. Приоритет по важности:\n" +
            "   text_input, button, link, dropdown, checkbox, radio.\n" +
            "   ИГНОРИРУЙ: логотипы, картинки, заголовки-тексты, декорации,\n" +
            "   боковые меню, footers, social-иконки.\n" +
            "9. description — НЕ БОЛЕЕ 150 символов. Одно предложение.\n" +
            "10. Ограничивай bounds только ключевыми элементами. Если сомневаешься —\n" +
            "    не включай элемент вообще.";

        /// <summary>
        /// Промпт для Planner LLM — выбирает следующее действие на основе задачи,
        /// истории и описания экрана. Отвечает JSON формата <c>VisionActionDto</c>.
        /// </summary>
        /// <remarks>
        /// См. DESIGN § 4.5.
        /// </remarks>
        public const string PlannerPlanNext =
            "Ты — Planner Agent IIChatTools. Твоя задача — управлять компьютером " +
            "через действия. Ты НЕ видишь экран напрямую — ты получаешь текстовое " +
            "описание UI от Vision-модели.\n" +
            "\n" +
            "На вход ты получаешь:\n" +
            "  - task: задача пользователя\n" +
            "  - history: список выполненных шагов (action + result)\n" +
            "  - screen: { description, ui_elements[] } текущего состояния\n" +
            "  - plan: список подзадач (можешь перепланировать)\n" +
            "\n" +
            "На выход — строго JSON, без markdown-обёрток:\n" +
            "{\n" +
            "  \"action\": \"click\" | \"double_click\" | \"right_click\" | \"move_mouse\" |\n" +
            "            \"type\" | \"press_key\" | \"hotkey\" | \"scroll\" | \"wait\" |\n" +
            "            \"done\" | \"fail\",\n" +
            "  \"target\": \"from_input\",        // ID из ui_elements (приоритет над x/y)\n" +
            "  \"x\": 100,                       // fallback, если нет target\n" +
            "  \"y\": 200,\n" +
            "  \"text\": \"Москва\",               // для action=type\n" +
            "  \"key\": \"Enter\",                 // для action=press_key\n" +
            "  \"keys\": [\"Ctrl\", \"C\"],        // для action=hotkey\n" +
            "  \"deltaY\": 300,                  // для action=scroll (+вниз / -вверх)\n" +
            "  \"reason\": \"Поле «Откуда»\"       // пояснение для UI/audit\n" +
            "}\n" +
            "\n" +
            "ПРАВИЛА:\n" +
            "1. Если задача выполнена — { \"action\": \"done\", \"reason\": \"...\" }.\n" +
            "2. Если невозможно — { \"action\": \"fail\", \"reason\": \"...\" }.\n" +
            "3. Приоритет: target > x/y. Используй target, если элемент есть в screen.\n" +
            "4. Не выдумывай target-ы, которых нет в ui_elements.\n" +
            "5. reason — 3-10 слов, для пользователя.\n" +
            "6. Отвечай ТОЛЬКО JSON, без пояснений.";
    }
}