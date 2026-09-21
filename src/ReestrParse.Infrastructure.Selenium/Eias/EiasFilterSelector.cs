using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Выбирает фильтры только через свежий DOM/JavaScript.
/// После загрузки страницы здесь намеренно нет IWebElement, чтобы динамический DOM ЕИАС
/// не мог породить stale element reference между поиском элемента и действием над ним.
/// </summary>
internal static class EiasFilterSelector
{
    public static void SelectHeatSupply(IWebDriver driver, WebDriverWait wait)
    {
        var selected = wait.Until(d =>
        {
            try
            {
                d.SwitchTo().DefaultContent();
                return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                    const normalize = s => (s || '').replace(/\s+/g, ' ').trim();
                    const heatText = 'Теплоснабжение';
                    const heatValue = 'WARM';

                    const selectors = [
                        "select[id*='Sphere' i]",
                        "select[name*='Sphere' i]",
                        "select[id*='sfer' i]",
                        "select[name*='sfer' i]"
                    ];

                    for (const css of selectors) {
                        for (const select of document.querySelectorAll(css)) {
                            const option = Array.from(select.options).find(o =>
                                normalize(o.value).toUpperCase() === heatValue ||
                                normalize(o.textContent) === heatText);

                            if (!option) continue;

                            // Требуется ровно одна сфера: WARM. Multi-select не должен
                            // сохранять случайные выбранные значения из предыдущего состояния.
                            for (const o of select.options) o.selected = false;
                            option.selected = true;
                            select.value = option.value;
                            select.dispatchEvent(new Event('input', { bubbles: true }));
                            select.dispatchEvent(new Event('change', { bubbles: true }));

                            if (window.jQuery) {
                                const $s = window.jQuery(select);
                                try { if (typeof $s.multiselect === 'function') $s.multiselect('refresh'); } catch (_) {}
                                try { if (typeof $s.selectpicker === 'function') $s.selectpicker('refresh'); } catch (_) {}
                                try { $s.trigger('change'); } catch (_) {}
                            }

                            return true;
                        }
                    }

                    const inputs = Array.from(document.querySelectorAll(
                        "input[value='WARM'], input[id*='Sphere' i], input[name*='Sphere' i], input[id*='sfer' i], input[name*='sfer' i]"
                    ));

                    for (const input of inputs) {
                        const id = input.id || '';
                        const label = id ? document.querySelector(`label[for="${CSS.escape(id)}"]`) : input.closest('label');
                        const labelText = normalize(label?.textContent);
                        const value = normalize(input.value).toUpperCase();

                        if (value !== heatValue && labelText !== heatText) continue;

                        if ((input.type === 'checkbox' || input.type === 'radio') && !input.checked) {
                            input.click();
                        }
                        input.checked = true;
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                        return true;
                    }

                    return false;
                    """));
            }
            catch (WebDriverException)
            {
                return false;
            }
        });

        if (!selected)
        {
            throw new NoSuchElementException(
                "Не найден фильтр сферы «Теплоснабжение». Организации с таким названием не используются для выбора фильтра.");
        }
    }

    public static void SelectGeneralOrganizationInfo(IWebDriver driver, WebDriverWait wait)
    {
        var selected = wait.Until(d =>
        {
            try
            {
                d.SwitchTo().DefaultContent();
                return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                    const select = document.getElementById('FormSelect')
                        || document.querySelector("select[name='FormSelect']");
                    if (!select) return false;

                    // В ЕИАС это одна option, value которой содержит сразу несколько
                    // form-id через ';'. Выбираем всю option целиком, не split-им value.
                    const option = Array.from(select.options).find(o =>
                        String(o.value || '').includes('F_W_O_4_1_1') &&
                        String(o.value || '').includes('F_W_O_1'));
                    if (!option) return false;

                    for (const item of select.options) item.selected = false;
                    option.selected = true;

                    const boxes = Array.from(document.querySelectorAll(
                        "input[name='multiselect_FormSelect']"
                    ));
                    for (const box of boxes) {
                        box.checked = String(box.value || '') === String(option.value || '');
                        box.setAttribute('aria-selected', box.checked ? 'true' : 'false');
                    }

                    select.dispatchEvent(new Event('input', { bubbles: true }));
                    select.dispatchEvent(new Event('change', { bubbles: true }));

                    if (window.jQuery) {
                        const $s = window.jQuery(select);
                        try { if (typeof $s.multiselect === 'function') $s.multiselect('refresh'); } catch (_) {}
                        try { $s.trigger('change'); } catch (_) {}
                    }

                    return Array.from(select.selectedOptions).length === 1 &&
                           select.selectedOptions[0] === option;
                    """));
            }
            catch (WebDriverException)
            {
                return false;
            }
        });

        if (!selected)
            throw new NoSuchElementException("Не найден фильтр формы «Общая информация об организации».");
    }
}
