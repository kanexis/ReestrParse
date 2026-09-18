using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReestrParse.WebBot.ChooseStrategy
{
    public interface IRegionNavigationStrategy
    {
        void Navigate(IWebDriver driver);
    }

    public class RegionNavigationStrategyFactory
    {
        private readonly Dictionary<string, IRegionNavigationStrategy> _strategies;

        public RegionNavigationStrategyFactory()
        {
            _strategies = new Dictionary<string, IRegionNavigationStrategy>
        {
            { "Алтайский край", new AltaiKraiNavigationStrategy() },
            { "Амурская область", new AmurOblastNavigationStrategy() },
            { "Архангельская область", new ArkhangelskOblastNavigationStrategy() },
            { "Астраханская область", new AstrakhanOblastNavigationStrategy() },
            { "Белгородская область", new BelgorodOblastNavigationStrategy() },
            { "Брянская область", new BryanskOblastNavigationStrategy() },
            { "Владимирская область", new VladimirOblastNavigationStrategy() },
            { "Волгоградская область", new VolgogradOblastNavigationStrategy() },
            { "Вологодская область", new VologdaOblastNavigationStrategy() },
            { "Воронежская область", new VoronezhOblastNavigationStrategy() },
            { "Донецкая Народная Республика", new DonetskPeopleRepublicNavigationStrategy() },
            { "Еврейская автономная область", new JewishAutonomousOblastNavigationStrategy() },
            { "Забайкальский край", new ZabaykalskyKraiNavigationStrategy() },
            { "Запорожская область", new ZaporozhyeOblastNavigationStrategy() },
            { "Ивановская область", new IvanovoOblastNavigationStrategy() },
            { "Иркутская область", new IrkutskOblastNavigationStrategy() },
            { "Калининградская область", new KaliningradOblastNavigationStrategy() },
            { "Калужская область", new KalugaOblastNavigationStrategy() },
            { "Камчатский край", new KamchatkaKraiNavigationStrategy() },
            { "Кемеровская область", new KemerovoOblastNavigationStrategy() },
            { "Кировская область", new KirovOblastNavigationStrategy() },
            { "Костромская область", new KostromaOblastNavigationStrategy() },
            { "Краснодарский край", new KrasnodarKraiNavigationStrategy() },
            { "Красноярский край", new KrasnoyarskKraiNavigationStrategy() },
            { "г. Москва", new MoscowCityNavigationStrategy() },
            { "г. Санкт-Петербург", new StPetersburgNavigationStrategy() },
            { "г. Севастополь", new SevastopolNavigationStrategy() },
            { "Кабардино-Балкарская Республика", new KabardinoBalkarOblastNavigationStrategy() },
            { "Курганская область", new KurganNavigationStrategy() },
            { "Курская область", new KurskNavigationStrategy() },
            { "Ленинградская область", new LeningradNavigationStrategy() },
            { "Липецкая область", new LeningradNavigationStrategy() },
            { "Московская область", new MoscowNavigationStrategy() },
            { "Мурманская область", new MurmanskNavigationStrategy() },
            { "Ненецкий АО", new MurmanskNavigationStrategy() },
            { "Новгородская область", new NovgorodNavigationStrategy() },
            { "Новосибирская область", new NovosibirskNavigationStrategy() },
            { "Орловская область", new OrelNavigationStrategy() },
            { "Пензенская область", new PenzaNavigationStrategy() },
            { "Пермский край", new PermNavigationStrategy() },
            { "Псковская область", new PskovNavigationStrategy() },
            { "Республика Адыгея", new AdigeyaNavigationStrategy() },
            { "Республика Алтай", new AltaiNavigationStrategy() },
            { "Республика Башкортостан", new BashkiriyaNavigationStrategy() },
            { "Республика Бурятия", new BuryatiyaNavigationStrategy() },
            { "Республика Дагестан", new DagestanNavigationStrategy() },
            { "Республика Калмыкия", new KalmikiyaNavigationStrategy() },
            { "Республика Карелия", new KareliyaNavigationStrategy() },
            { "Республика Коми", new KomiNavigationStrategy() },
            { "Республика Крым", new KrimNavigationStrategy() },
            { "Республика Марий Эл", new MariyELNavigationStrategy() },
            { "Республика Мордовия", new MordoviyaNavigationStrategy() },
        };
        }
        public IRegionNavigationStrategy GetStrategy(string regionName)
        {
            if (_strategies.TryGetValue(regionName, out var strategy))
            {
                return strategy;
            }

            throw new ArgumentException($"Навигационная стратегия для региона '{regionName}' не найдена.");
        }
    }


}
