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

                            if (!select.multiple) {
                                for (const o of select.options) o.selected = false;
                            }
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
                    const checkbox = document.getElementById('ui-multiselect-FormSelect-option-10')
                        || document.querySelector("input[name='multiselect_FormSelect'][value*='F_W_O_4_1_1']");

                    if (!checkbox) return false;

                    if (!checkbox.checked) checkbox.click();
                    checkbox.checked = true;
                    checkbox.dispatchEvent(new Event('input', { bubbles: true }));
                    checkbox.dispatchEvent(new Event('change', { bubbles: true }));

                    const value = checkbox.value || '';
                    const name = checkbox.name || '';
                    let select = null;

                    if (name.startsWith('multiselect_')) {
                        select = document.getElementById(name.substring('multiselect_'.length));
                    }

                    if (!select && value) {
                        const option = Array.from(document.querySelectorAll('select option'))
                            .find(o => o.value === value);
                        select = option?.parentElement || null;
                    }

                    if (select) {
                        for (const option of select.options) {
                            if (option.value === value) option.selected = true;
                        }
                        select.dispatchEvent(new Event('input', { bubbles: true }));
                        select.dispatchEvent(new Event('change', { bubbles: true }));

                        if (window.jQuery) {
                            const $s = window.jQuery(select);
                            try { if (typeof $s.multiselect === 'function') $s.multiselect('refresh'); } catch (_) {}
                            try { $s.trigger('change'); } catch (_) {}
                        }
                    }

                    return checkbox.checked === true;
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
