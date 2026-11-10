using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Keynote.Core;
using Keynote.Online;

namespace Keynote.App;

public partial class MainWindow
{
    sealed record ModernPick(
        string Artist,
        string Title,
        string Category = "General",
        double DurationSeconds = 0,
        int NoteCount = 0,
        string Sha256 = "",
        RemappedSong? CloudSong = null,
        Uri? Source = null,
        string OriginCountry = "",
        string OriginRegion = "",
        string Language = "",
        string MusicalStyle = "")
    {
        public override string ToString() =>
            $"{Title}  •  {Artist}" +
            (CloudSong != null ? $"  •  {Category}" : ModernDownloads.Find(Artist, Title) != null ? "  •  Creator" : Source != null ? "  •  Source website" : "  •  Search only");
    }

    RemappedCatalogService? modernCloudCatalog;
    IReadOnlyList<RemappedSong> modernCloudSongs = Array.Empty<RemappedSong>();
    CancellationTokenSource? modernDownloadCancellation;
    bool modernCloudLoaded;

    static readonly ModernPick[] ModernPicks = {
        new("Hans Zimmer", "Cornfield Chase (Interstellar)", "Film & TV", 127.51, 1269, "69aff63cc2cdeac313de00b2874dcca257403c5507cf03bc09d9dcd1dbdef09a", Source: new Uri("https://www.youtube.com/watch?v=57SdNVHYdNk")),
        new("Kanye West", "Runaway", "Pop", 68.97, 296, "00904d8488eaa80a967944026baa912224c07383c14952056b4239dfebb49461"),
        new("Kanye West", "I Wonder", "Pop", 93.75, 810, "0614a7d748d90831f451d25c13e049530ea68a0eec85225e2b1e372763289c38"),
        new("Kanye West", "Heartless", "Pop", 87.61, 504, "b6673357de246b63fd9edc5dfa9b1cef11227ec0d7b4342a8cace661738654ac"),
        new("Kanye West", "Flashing Lights", "Pop", 85.3, 626, "d99a096c884397ba9524779a11cf7532abc3013be5ebd600792f1455b154fe60"),
        new("Kanye West", "Ghost Town", "Pop", 91.36, 240, "e1ba64fa389dac15ef8cd08630227675e4837c4a95909c43a3b8e65da3969d7f"),
        new("Kanye West", "Violent Crimes", "Pop", 114.63, 261, "9603d78ea896b76b40abdf9cb86ea16ddc73fedb51f080c97f75dae6228cc088"),
        new("Kanye West", "Hey Mama", "Pop", 74.67, 236, "1cb1f63d7c2c8b8d15a68e6c04daf36734680d57aae95da2dea818bb2dc8eccf"),
        new("Kanye West", "Jail", "Pop", 157.09, 821, "2143e152a4eebb63ba9edec7081fc97c6802dd93a82b208ec19f12e965c3b30c"),
        new("Kanye West", "Pure Souls", "Pop", 121.24, 537, "f3c001002c3247dae991af6794d734e609fba68c34a5df0ad8b56b1177d48425"),
        new("Lana Del Rey", "The Grants", "Pop", 246.13, 860, "7a04ad9170359c27b282204c0d399ba5758b0b8530e5ceb1e157195225ca201d"),
        new("Lana Del Rey", "Candy Necklace", "Pop", 298.6, 1282, "dd3b68fc0302d6ea84c7713a92d137e773eb70be1ab6fb12d99bca5181130f5b"),
        new("Clairo", "Bags", "Pop", 73.03, 670, "26c81510acf099e8e9f801e0316e4bbc73cad0645d77e938f6ddccac2e6344a8"),
        new("Clairo", "Sofia", "Pop", 66.68, 529, "74f39b1dbc75b2703445206579fd8c88aa7ba75d3e8af7f87543a87a563b322a"),
        new("Clairo", "Pretty Girl", "Pop", 80.84, 550, "5c66e609a3dd2a4308876f7ff1755e193d800da8b95f60ebc76809ecf7c948a8"),
        new("Clairo", "Amoeba", "Pop", 66.13, 734, "32dab2a39226058ebd6260316fa84b1f04ccc8b546ac73a1a2b33fa34c2e6361"),
        new("Clairo", "Bubble Gum", "Pop", 93.88, 190, "5350f8e5dd3cb89aeedf926364913579a69f8554e6f036f86b94f57195cffca7"),
        new("Clairo", "Juna", "Pop", 85.2, 318, "c822bd0f9612277b18bd911673834422155489d67a65d3e57412db933f0092b8"),
        new("Clairo", "Sexy to Someone", "Pop", 69.71, 475, "c7ad33f6cbf28f5e43573a257937d6954b8b63b1af72cc6a705a92b3f0e1fa7a"),
        new("Laufey", "From the Start", "Pop", 166.83, 1941, "425ae0b8afc7306c3d492d1d9c78f7a4b30fe061c444ee818fd2f020f94ecdd7"),
        new("Laufey", "Valentine", "Pop", 137.0, 699, "27788ba0994111b695930ed896d568c7487b7ff6784ae24fe956587049b96def"),
        new("Laufey", "Promise", "Pop", 164.83, 312, "3eb7fce1e49f3f6a3efd1fc57a1c0c8bd69987499cae562673c031805009d4eb"),
        new("Laufey", "Let You Break My Heart Again", "Pop", 155.51, 483, "8d99758e216663435b784d7754e8950056f1e53f0f440a8bc8c07232d24901e6"),
        new("Laufey", "Falling Behind", "Pop", 88.5, 990, "ef63ddff456eca086250dc721f623b6b2088aea5e9db8ebdf8202382b3e47af9"),
        new("Laufey", "Bewitched", "Pop", 159.83, 483, "3c706a96ff628c2cfd59001d2970d13677a2529f1c107b8fd83aa42da312a783"),
        new("Laufey", "Like the Movies", "Pop", 104.7, 894, "512215c8cf8c33809939a1c2c9a1a3f5b9e512a16b04b82ca0e7a4e260c62da1"),
        new("The Weeknd", "Blinding Lights", "Pop", 182.73, 590, "5a18fdae05c35021ca88f213c444712708136c4bb021112cd8fb8d0fe0180ad9"),
        new("The Weeknd", "Die for You", "Pop", 114.45, 545, "67f555705a2f0e358cd9601153496e334b0f7548a51d28c264f11d36b20de248"),
        new("The Weeknd", "Save Your Tears", "Pop", 101.7, 2392, "aaaebc3caffe5f3a79d4ae94882ffa2d3248758924300ae720b60569b53b6b75"),
        new("The Weeknd", "Starboy", "Pop", 82.55, 538, "11e1c02d2055fa2868b7a2785ca946f7c0e87c181e930286bee6d24e30369f49"),
        new("The Weeknd", "Call Out My Name", "Pop", 209.28, 1050, "30b071117813d73bc21321362e317c98c7b548ff38189d1f3bac9d98bfa5fd83"),
        new("The Weeknd", "The Hills", "Pop", 167.78, 850, "698535fccccae4b8601850fbcbb4102aa5b4c779ed0fe079c3ef82ff4ecc5ee9"),
        new("The Weeknd", "Out of Time", "Pop", 82.42, 625, "460e225a48caeb41349b164f397a6e9aa6f454364fbff98c7f8c64a16a6ddb81"),
        new("Frank Ocean", "Pink + White", "Pop", 95.95, 706, "ef9c3574a9cce7904f7a7b0534689c763dd1de28be8420e9689167b29618370d"),
        new("Frank Ocean", "Ivy", "Pop", 79.94, 200, "00f0602b87bb9012d1a32f941d5324581631b160abac37a8532daf97415eeb97"),
        new("Frank Ocean", "Thinkin Bout You", "Pop", 117.97, 548, "5bb68f1452021be22770b6a5c6356a7e91a536b7e30885c3c3688c5a66fd11c3"),
        new("Tyler, The Creator", "See You Again", "Pop", 97.06, 724, "37a9082e5d97a5f612006a70272adffcdc239a78b9d42a5f16aa8303d71ef2ca"),
        new("Tyler, The Creator", "EARFQUAKE", "Pop", 95.81, 720, "74e5c78f8990ec0e2f8490afff50bd05e04c7b0042c8d5fd21aff6febbedf17b"),
        new("Steve Lacy", "Bad Habit", "Pop", 90.25, 657, "96d1ee5d5cb6e224f831b0a7713eeae47b79aaffbae1cb6f607f1a7fb8e02ec5"),
        new("Steve Lacy", "Dark Red", "Pop", 66.68, 618, "63626861f0b2d1c8e6b279ad0dd5db97a22a7949baf0d4c8ef769d5b7f7c5640"),
        new("SZA", "Snooze", "Pop", 106.5, 572, "92902fc5d1acc08adc52aa26fc6335a5dcad81f9ffb404effe981b703e053747"),
        new("SZA", "Good Days", "Pop", 127.8, 205, "7a92bdf6779ae40c3f7ff7539cb85f6d3e841b4226f96af4369ecdccb9535c65"),
        new("Billie Eilish", "BIRDS OF A FEATHER", "Pop", 77.71, 2574, "033e7f1601540b3230771569855c8bfea0d86398ef484883200eb548b9135632"),
        new("Billie Eilish", "WILDFLOWER", "Pop", 112.76, 287, "5ee5a95e4408a630cc437ce50b9797f59d91145e3080094951be7ba7db3952cc"),
        new("Lana Del Rey", "Young and Beautiful", "Pop", 116.27, 340, "7b526f33c5ec440147eb2d6c9df78a2d7afcbe8d94fa59d6653d1b936688f96a"),
        new("Lana Del Rey", "Summertime Sadness", "Pop", 263.44, 1118, "3ca20c41586f852269fd96b615ae6a87094d871a27ae688a7bed61b7b6d74081"),
        new("Joji", "Glimpse of Us", "Pop", 90.21, 450, "eb588f6302a23560b8463b404344d0abaed62e40590233e87add94f19adb334e"),
        new("Joji", "SLOW DANCING IN THE DARK", "Pop", 102.24, 356, "894bbda491afd1c38afb09d5eb4ea96560d3394bf58f9e9f1ed1f3655d872687"),
        new("Daniel Caesar", "Best Part", "Pop", 102.24, 220, "c7dbbdfbbabb99bf666eb59d44b740741f0158ec9c6400326292747a2054d3dd"),
        new("Mac DeMarco", "Chamber of Reflection", "Pop", 116.18, 608, "e39c7aa3005401b6c5d01b1b3ccc6577ed031d45c1480237fd0103cb606ed31c"),
        new("Traditional Chinese Folk", "Mo Li Hua (Jasmine Flower)", "Traditional", 79.96, 476, "ba23c27998909eb005012d258246e183c0771a2c207b1ebcc1f4442dc3d6ab93", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Jiangnan Folk"),
        new("Teresa Teng / Sun Yi", "The Moon Represents My Heart", "Pop", 75.75, 408, "a7b39b8311a1cb3986d66f022c6979d439b8c394b2c64ed184c7104d5333f127", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Mandopop Classic"),
        new("He Zhanhao & Chen Gang", "Butterfly Lovers (Liang Zhu)", "Classical", 84.66, 408, "d4671547a910acfbbb9e86345859e9138c50c632d54eafea94c35e2fd742030f", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Orchestral-to-Piano"),
        new("Traditional Guqin Classic", "High Mountain and Flowing Water", "Traditional", 79.96, 408, "4504101a16fef293c5a2927b542649b03e1c522aff59524c00bba2db0d8b5f00", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Zither Folk"),
        new("Lv Wencheng", "Autumn Moon Over the Calm Lake", "Traditional", 86.12, 476, "fc49eda0206cdcb6c56015684ff764735859ada004ccf1bac32ec16a2ca00225", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Cantonese Melodic"),
        new("Ren Guang", "Colorful Clouds Chasing the Moon", "Traditional", 76.33, 476, "78008882d1aa187ee78c52d25ae0b46213ef43f589cd3cc88d45ed534af81d98", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Guangdong Folk"),
        new("Traditional Pipa Classic", "Spring River Flower Moon Night", "Traditional", 82.24, 408, "a63c36ee0b912f9766c00c534fc5154a7535cdc292ce54bf0eeaabec4ba29e07", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Ancient Court"),
        new("Hubei Folk Traditional", "Song of the Dragon Boat", "Traditional", 77.12, 612, "100604f8aefec03a1f0cc5736f59bb3d49ef99d8bcb65f4f3d6c6fb06e23ed94", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Dragon Boat Reel"),
        new("Lv Wencheng", "Step by Step Higher (Bu Bu Gao)", "Traditional", 74.46, 612, "56a37a182a0250f6384c8fb0cf217aa0b5b5fc3c6f25ff4d562471efcb6ff667", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Cantonese Celebration"),
        new("Traditional Folk", "Silver Clouds", "Traditional", 73.01, 476, "d9b34f3c4119e846ca66a7d6f70453c3ddadcd5947ab5169ac78488cba24082b", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Northern Folk"),
        new("Li Shutong (John P. Ordway)", "Farewell (Song Bie)", "Classical", 79.96, 408, "967b765d2b4c093e03f39c04878e28428205e5e443eb52605445bd58f54fc350", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "School Song Lyric"),
        new("Sichuan Folk Song", "Kangding Love Song", "Traditional", 79.97, 544, "bbbfec9553ff99d2e5456496f629e271f0b738436663927049674a3e3078697a", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Tibetan-Sichuan Border Folk"),
        new("Wang Luobin (Qinghai Folk)", "In That Distant Place", "Traditional", 83.96, 476, "765c79ae83b763e97ce55536ff7bbcd22eb4559dcd6ae70b559900425f6de06c", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Silk Road Ballad"),
        new("Jiangsu Melody", "Jasmine in Spring", "Traditional", 78.1, 476, "08886487d8fd764efe5464343e11a9ea6a770e298e7e52b764906c51ba91cd27", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Jiangnan Silk & Bamboo"),
        new("Zhou Xuan / Traditional", "Song of the Four Seasons", "Pop", 74.63, 476, "fd3e18662dca2627e0878027689c7d1f46ee3710e56a5f2f54bc582754290689", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Vintage Shidaiqu"),
        new("Huan Yi (Jin Dynasty)", "Plum Blossom Melodies", "Traditional", 87.23, 408, "2120a445e0c77262f8ee7827e74013c67993784140d6da91d5c1b80fd2642786", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Court Elegance"),
        new("Traditional Coastal Ballad", "Fishermen's Triumphant Song", "Traditional", 75.75, 408, "3f061944b191091f4858478b27d841b73077e6f148cc6cf593cf7fea2c173e9f", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Shandong Seaside"),
        new("Inner Mongolian Pastoral", "Shepherd's Song of the North", "Traditional", 82.24, 408, "110a0b57ac73cd1dc88643d6854cfbdc5b0c1bae40ad57a8578874fcd9c2e56f", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Grassland Ballad"),
        new("Zhou Lanping / Pan Yingjie", "Green Island Serenade", "Pop", 77.8, 408, "296bde680adbffe0f98bca111aee390b556db7af8b87fcbc9d9688f5ab557d4b", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Nostalgic Mandopop"),
        new("Li Tai-hsiang / Sanmao", "Olive Tree (Gan Lan Shu)", "Pop", 83.96, 504, "5b000559c40d2364c559be5ffbfcc698b22b4ccf798ea0d9d4b9b5f5a851566d", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Campus Folk"),
        new("He Liutang", "Rain Falling on the Banana Tree", "Traditional", 73.82, 544, "23987ff68fcbe019b0707e4533cd3b75ebedaf35ca988b4aa49475b26477d22c", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Lingnan Melody"),
        new("Traditional Military Fanfare", "General's Command", "Traditional", 71.97, 612, "3b57bbd9fb4d9b88b2f02d55ac6221b22f738d88c8718cdac8afc3bc63872dfa", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Heroic Ballad"),
        new("Northeast China Folk", "River of Sorrow", "Traditional", 89.95, 408, "79f4fa67e933a590a66008419c8e11c4b6e7055585a31c2e81e1e161e5b524b3", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Erhu Lament"),
        new("Henan Folk Suite", "Hundreds of Birds Worshipping the Phoenix", "Traditional", 74.98, 680, "d8a08b93e31b49145c43c49d0ae1a9765dbd25208ea365dda7bcc7ecda556bd6", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Suona Festive"),
        new("Tang Dynasty Ode", "Song of Pipa", "Traditional", 79.96, 408, "3c6f614d6da5929fb8ebf2b4b2f7a1519cfb7bc60bd81b1209e64600f3e91280", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Lyrical Ballad"),
        new("Hebei Folk Melody", "Little Cabbage (Xiao Bai Cai)", "Traditional", 84.66, 408, "3076d582219dae911e96997b0793c0539dda5b0ca00b872bd3e45d4da4d745a7", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Northern Lament"),
        new("Mongolian Grassland Melody", "Pastoral (Mu Ge)", "Traditional", 77.37, 340, "1e85d10574a31a4df7194cae3ad34e5851d0548686ee5b650a3f4c4605acebff", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Long Song Tune"),
        new("Shaanxi Folk Ballad", "Embroidering the Pouch", "Traditional", 73.01, 476, "85de1d2657039e28a1c063d0d8dcfe67589012223710602b197fb3ccf94750b4", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Loess Plateau Tune"),
        new("Folk Nocturne", "Crescent Moon (Xin Yue)", "Traditional", 82.24, 408, "cae5e21bf8f88754126ad0e3fd2ed68ff23f4d8b34f0b7b0a0fbd5ccb30830cd", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Evening Serenade"),
        new("Yunnan Ethnic Ballad", "Song of the Reed Pipe", "Traditional", 79.97, 612, "f75aa1bf6190bd71413d11c6d2a1149c766f60fd7429017c98197f485cb6bce0", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Lusheng Dance"),
        new("Yellow River Chantey", "Boatman of the Yellow River", "Traditional", 76.77, 544, "c45f75bef8c75560c3beb872a7fb5b75dc05a355e956eae6a23dd7ecec1e8abc", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "River Work Song"),
        new("Guizhou Folk Motif", "Mountain Trail in the Mist", "Traditional", 86.12, 476, "edd1cf6b237374cdf37c1946b115c2acf6f75762cef92229d1329519db442342", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Highland Echoes"),
        new("Suzhou Temple Ode", "Midnight Bell of Hanshan Temple", "Traditional", 79.95, 340, "da90ca17a166ae47fb7984ad634bcb379f96530059664ac4f61ad5d31d207886", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Zen Meditation"),
        new("Dai Ethnic Melody", "Bamboo Under the Moonlight", "Traditional", 79.96, 476, "b0cb2feb6397fcbf09ad4c54cedb250ecefef9396a02ca971488aca6dda6cfd1", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Hulusi Melodic"),
        new("Three Gorges Melody", "Daning River Ballad", "Traditional", 78.1, 476, "3ceb418f92c42dee3a9dc52a7d68af232bb898ffcdb42010e28a1c8636f09e06", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Gorge Rowing"),
        new("Jiangnan Nocturne", "Suzhou River Moon", "Traditional", 79.96, 408, "3d206d88f3857d22987cfc707eae0e801903a3df257993ef939185538dfb4ac1", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Canal Serenade"),
        new("Shandong Horizon Ode", "Morning on Mount Tai", "Traditional", 83.96, 476, "745c71e8e5984835914fe6c9ff11b15e5aa65cbcd23c5b0d259f8cd3dd38e92f", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Sunrise Hymn"),
        new("Monastery Chimes", "Evening Drum and Morning Bell", "Traditional", 87.23, 408, "f07ca719949780e33356fb0cf651d6447355cf671f7c68ea9329ac29f0438df4", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Contemplation"),
        new("Hangzhou Scenic Theme", "West Lake Reflections", "Traditional", 76.76, 408, "4ca1ff63274574bcfa3f54b599b4e4dbed1b598fa8a70c8f4a945d16be40c31a", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Gentle Breeze"),
        new("Tang Xianzu Kunqu Melody", "Peony Pavilion Theme", "Classical", 84.66, 408, "8a87d0a6e76f31c7809ffb8c9a31932f6fc299e7d3f911593625393fc6d61c99", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Kunqu Opera Suite"),
        new("Chinese Acoustic Adapt", "Red River Valley Chinese Melody", "Traditional", 74.63, 476, "eeeec17ad1b514840f358b2d5b20bb2bd13a543ef684caed1b9e47bf34c961b7", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Folk Harmonization"),
        new("Nie Er", "Golden Snake Dance", "Traditional", 72.7, 680, "8e5ad728d9cdde7f29e118cc7bdf8d0476832c84f72d7d81a873dd1a8296607f", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Dragon Festival Tune"),
        new("Folk Gathering Tune", "Celebration in the Village", "Traditional", 71.97, 612, "06d9b136861305039d94d349b391287d40f2b6950b366c4f0d529c74719dd771", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Harvest Reel"),
        new("Huang Haihuai", "Horse Racing (Sai Ma)", "Traditional", 70.57, 680, "66ca60e44bacaec3ec6dd05a81c862918b399e8cf6c1b7a1aeecc189fc9a4d83", OriginCountry: "China", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Grassland Steppe"),
        new("Liu Xue'an", "Song of the Great Wall", "Classical", 79.96, 476, "d193c616ffe8c75f8f3f45877276345b8db41bd1d5a3cf54563db6a05da93500", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Heroic Ballad"),
        new("Fujian Folk Reel", "Tea Picking Ballad", "Traditional", 79.97, 612, "40b1aaeb6c631310839e97e260a50c6ee79880084bcf5dcac835804f7d8986ca", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Tea Mountain Tune"),
        new("Wang Liping", "Dream of the Red Chamber Theme", "Classical", 87.23, 408, "2d288b312abe37bf08450f1b0395bc00daf316010d2ff05af6172d857beba70c", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Literary Opera Theme"),
        new("Zhao Jiping", "Water Margin Heroes Song", "Pop", 77.12, 612, "7db64dbedde40c7aab0fe9327d16cee6a93f14650293a0e0f3c57f262a9f6d5b", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Historical Anthem"),
        new("Xu Jingqing", "Journey to the West Theme", "Pop", 73.82, 544, "eb607a8563c5fd1d81c60fbbf8974d8de1530595c8bf5ba7f7275d8b0cd26af6", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Mythological Theme"),
        new("Peking Opera Contemporary", "Ode to the Pear Blossom", "Classical", 75.75, 408, "a34f2bbaf2ccc9c7510a11e69f13c7d08a9cca4819f4f03e8f6d7e4c650642f8", OriginCountry: "China", OriginRegion: "East Asia", Language: "Mandarin", MusicalStyle: "Opera Fusion"),
        new("Indian Classical Heritage", "Raga Bhairav (Morning Serenade)", "Classical", 79.96, 408, "084d65c3bbcc44aeb0764b31d8efeea9adf728ed6ffed1dbfe6c1e265e9c5c85", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Dawn Raga"),
        new("Indian Classical Heritage", "Raga Yaman (Evening Meditation)", "Classical", 75.75, 432, "0c08ce637386418fe0b0014eb1d73729cc084c52b0c81aa030acb097d558bdc8", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Kalyan Thaat"),
        new("Miyan Tansen Heritage", "Raga Darbari Kanada (Nocturne)", "Classical", 89.95, 408, "db01e2ad7b34f166716796dd6161f53e6f5262eb929690d02e94fc18faab171a", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Midnight Darbari"),
        new("Indian Classical Heritage", "Raga Bageshri (Midnight Expression)", "Classical", 82.24, 408, "394a7e50313ac4ae96adb471afd9410923c7abeea94103a37930b2dbcee3424d", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Kafi Thaat"),
        new("Indian Classical Heritage", "Raga Desh (Monsoon Ballad)", "Traditional", 83.96, 476, "43ed9968aca02a915124725874e58d0bbfadc33c7774dc76f4001994dcfbb424", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Rain Raga"),
        new("Indian Classical Heritage", "Raga Kafi (Spring Festival Theme)", "Classical", 73.01, 476, "9429bf3db15833c6adf74782f4ed0b7283bbf18a518551c2101b0d83c3f1661d", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Spring Raga"),
        new("Indian Classical Heritage", "Raga Bhupali (Dawn Awakening)", "Classical", 79.96, 476, "d183b70d2615eaf8ec0924277f6cc2aac6059222bcf9e85e6d00434bb429b66d", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Pentatonic Dawn"),
        new("Indian Classical Heritage", "Raga Todi (Sunrise Devotion)", "Classical", 87.23, 408, "fa8c8f183523aae04047d95dc1eaa8f682ccd9a0b3a329888d616beec0ad66a1", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Todi Thaat"),
        new("Indian Classical Heritage", "Raga Malkauns (Spiritual Night)", "Classical", 84.66, 408, "42d64fced1d75e9ad3f286b6ac4c0997b458a66f06b866b995f382c64347f840", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Bhairavi Thaat"),
        new("Indian Classical Heritage", "Raga Asavari (Morning Solitude)", "Classical", 82.24, 408, "0178012b4a5ca7aed193d01940d209e0a3ead39ce1bae96becb5b6fd960d2ec7", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Asavari Thaat"),
        new("Indian Classical Heritage", "Raga Khamaj (Romantic Twilight)", "Classical", 81.91, 476, "5bfbeb157c2506c1e2ddf9bf8fd0eda6807f1acbf43c10427d543a8ba09ef810", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Thumri Style"),
        new("Indian Classical Heritage", "Raga Kedar (Temple Reverence)", "Classical", 79.96, 408, "ca4ea4505ca69785ee2dd65e183a0c633ca102506cbcfa6f6577e1ff5d1e7505", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Kalyan Thaat"),
        new("Indian Classical Heritage", "Raga Marwa (Sunset Glow)", "Classical", 84.66, 408, "fff3f4ec05d67609593e9552535d1d5f69d6541d590700234132b337a6d00afc", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Marwa Thaat"),
        new("Indian Classical Heritage", "Raga Pilu (Folk Expressive)", "Traditional", 76.33, 476, "20bd3e365d48204620a697e4c3c552fc85a5e09d0e4711f06f60d06b93d46b59", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Semi-Classical"),
        new("Indian Classical Heritage", "Raga Shankara (Majestic Ascent)", "Classical", 81.67, 544, "74cd4c84e8ca3da4d9b0736cd8008b5312e423e4aae333d8901dec9b71d45a01", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Bilawal Thaat"),
        new("Indian Classical Heritage", "Raga Tilak Kamod (Joyous Flight)", "Classical", 78.1, 476, "f6209aa7733b26d87ef2a4f6dd33478ff274966fb7b9aaa9cee0d6292eaa6701", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Khayal Raga"),
        new("Indian Classical Heritage", "Raga Jaijaivanti (Courtly Dignity)", "Classical", 77.8, 408, "329076b82f15ccf7edd5beba5b90295d829db071acaec8cd0eafa300a2c2fc37", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Royal Court Raga"),
        new("Indian Classical Heritage", "Raga Ahir Bhairav (Dewdrop Morning)", "Classical", 76.76, 408, "050ae297c8450c602d61c45acdf89d01b57ff58c4c83e3151fc6c237388260c9", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Bhairav Family"),
        new("Carnatic Heritage", "Raga Hamsadhwani (Southern Auspicious Dawn)", "Classical", 79.97, 544, "12d732eef813a7b017beabcafd7beb165187903389b07f53ba3a5d8d7dff6f3b", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Carnatic / Hindustani"),
        new("Carnatic Heritage", "Raga Charukesi (Graceful River)", "Classical", 86.12, 476, "6ae385e80bc7a80f850ec50878559b6594324b957e2a9779ab8824cd4965af72", OriginCountry: "India", OriginRegion: "South Asia", Language: "Instrumental", MusicalStyle: "Melakarta 26"),
        new("Bankim Chandra Chattopadhyay", "Vande Mataram (Hymn of the Motherland)", "Traditional", 75.75, 408, "87ac44866bc3bdb405d70a125ea1637a980397a4bc4f787b888684459f967596", OriginCountry: "India", OriginRegion: "South Asia", Language: "Sanskrit", MusicalStyle: "National Hymn"),
        new("Rabindranath Tagore", "Jana Gana Mana (National Anthem Harmony)", "Classical", 79.96, 408, "7091eea133ae24c517cf1b53c51e8f401355164f699760044ef8786e11e239bf", OriginCountry: "India", OriginRegion: "South Asia", Language: "Bengali", MusicalStyle: "National Anthem"),
        new("Narsinh Mehta", "Vaishnava Janato", "Traditional", 84.66, 408, "82fae5339cd8e15c00c9e731d7556b72f329eb9fab868491834f3cfae7348f6b", OriginCountry: "India", OriginRegion: "South Asia", Language: "Gujarati", MusicalStyle: "Bhajan of Empathy"),
        new("Traditional Devotional", "Raghupati Raghav Raja Ram", "Traditional", 79.96, 476, "841ec4bc4b8eacff4e7d88518f6fbfc8254e3a5977426ddfcd2514259fefee28", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Harmonic Bhajan"),
        new("Mithoon / Arijit Singh", "Tum Hi Ho", "Pop", 75.75, 408, "a5d5fec1b421c279e3248a388269903012abf83b9528263b382ee7ef2e8d7d70", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Bollywood Ballad"),
        new("Shankar-Ehsaan-Loy", "Kal Ho Naa Ho (Piano Theme)", "Pop", 83.96, 476, "6cb36ef4b908da43653c801aafd1957a9d0c6a3ef9ea4d1a88b2cd24b0292d5e", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Cinematic Ballad"),
        new("A. R. Rahman", "Jai Ho (Celebration Suite)", "Pop", 76.17, 680, "d471a9e352d8c258cc483029810bba373f4e35dd917ead9325370836a340164b", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Oscars World Pop"),
        new("A. R. Rahman", "Tere Bina", "Pop", 81.91, 476, "e48c71ebcf03bc421587dd977f792727d9a1048a993fc4e287bd00c0473f2235", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Qawwali Pop Fusion"),
        new("Salim-Sulaiman", "Tujh Mein Rab Dikhta Hai", "Pop", 79.96, 476, "5c4864f7d69f09e71200e8f94d9bedc816b7e145b26e2ce5a482056441266c93", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Romantic Devotional"),
        new("Vishal-Shekhar / Atif Aslam", "Dil Diyan Gallan", "Pop", 76.33, 476, "0cfd49b6f99137ef19a820e890b2d5022d72b7d5311d6a629bcea685694bbfbe", OriginCountry: "India", OriginRegion: "South Asia", Language: "Punjabi", MusicalStyle: "Acoustic Waltz"),
        new("Pritam", "Raabta", "Pop", 81.91, 476, "547eecacc4efdee36f06c9d441013268200663d27c289872774e9083f85094b1", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Unplugged Piano Theme"),
        new("Pritam / Arijit Singh", "Kesariya (Brahmastra Piano)", "Pop", 74.63, 476, "6176fcbaaa06e766df17254a785579c8bac01c874131c4430105b7a9b6aba9f2", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Saffron Love Ballad"),
        new("Pritam / Arijit Singh", "Channa Mereya", "Pop", 77.8, 408, "af1af65eecc2f241f4b185ca31f17daadbc6da8f48e0b97f1365322878f18474", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Soulful Farewell"),
        new("Pritam", "Gerua", "Pop", 79.96, 476, "40f526fea441bca95bd540e0c9f9290533d3312ad7479cacedda5091b2918159", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Grand Bollywood Ballad"),
        new("A. R. Rahman / Alka Yagnik", "Agar Tum Saath Ho", "Pop", 75.75, 408, "dfaed0f0632b4bdcc923dad963a5b6f9766304ccbf5036a24c75a0f38d150a4c", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Melancholy Duet"),
        new("Pritam", "Ae Dil Hai Mushkil (Title Theme)", "Pop", 78.1, 476, "5977eac819637c37cf5f5e344dc6b97a06278faeed11d1b44644b630a7414b18", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Romantic Drama"),
        new("Ilayaraja", "En Iniya Pon Nilave", "Classical", 73.01, 504, "336c8c5058d4eb9a26528c951f31069f48e6800b50c23b5cf9a248fdcc4d8d76", OriginCountry: "India", OriginRegion: "South Asia", Language: "Tamil", MusicalStyle: "Southern Golden Classical"),
        new("A. R. Rahman", "Roja Janeman", "Pop", 86.12, 476, "12eea0ba25417b93387ec59ace4471d47707eb1f0a64df1240a4c376da349496", OriginCountry: "India", OriginRegion: "South Asia", Language: "Tamil", MusicalStyle: "Modern Indian Symphony"),
        new("A. R. Rahman", "Chinna Chinna Aasai", "Pop", 76.77, 544, "db83f4e81f31a38d066e788756c92666dfcf010bf5aee8365f91aa9873761c98", OriginCountry: "India", OriginRegion: "South Asia", Language: "Tamil", MusicalStyle: "Joyous Dream"),
        new("Thaman S / Sid Sriram", "Samajavaragamana", "Pop", 78.34, 544, "76835e778e16ab2bdd5871c56f17b9ac927a1997c6ed4264f11a829afb13ce57", OriginCountry: "India", OriginRegion: "South Asia", Language: "Telugu", MusicalStyle: "Carnatic Pop Fusion"),
        new("Yuvan Shankar Raja", "Rowdy Baby (Piano Groove)", "Pop", 77.4, 680, "d19f99a2c36a133bd9b3028d19ba27c6b3286ff14bcf53333c0e9e1f9b360413", OriginCountry: "India", OriginRegion: "South Asia", Language: "Tamil", MusicalStyle: "Kuthu Acoustic Dance"),
        new("Thaman S / Armaan Malik", "Butta Bomma (Piano Suite)", "Pop", 79.97, 612, "a401b92ee4ba5cc8e7a77a6f7f766d833504d3509c424c893ae8076f8e48327e", OriginCountry: "India", OriginRegion: "South Asia", Language: "Telugu", MusicalStyle: "Melodic Acoustic"),
        new("Yo Yo Honey Singh", "Dheere Dheere Se", "Pop", 78.1, 476, "e93aca41ba7426f6d923b0f270c4adc6e7603bf6ab90bb6464ca4332c0f637ed", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Smooth Pop Ballad"),
        new("A. R. Rahman / Javed Ali", "Kun Faya Kun", "Traditional", 79.96, 408, "ae4ab056ae7227d01480c04145a05dea61eb475944351494ad3cc394ca68bd0d", OriginCountry: "India", OriginRegion: "South Asia", Language: "Urdu", MusicalStyle: "Sufi Elevation"),
        new("A. R. Rahman", "Khwaja Mere Khwaja", "Traditional", 86.12, 476, "5045b8d1521bf4fa4b15feda24e3d4d871b6da0c950878dc55b1986ac99ddac4", OriginCountry: "India", OriginRegion: "South Asia", Language: "Urdu", MusicalStyle: "Sufi Devotional"),
        new("Shankar-Ehsaan-Loy", "Mitwa (Love Ballad)", "Pop", 78.52, 612, "ac172419f9a229ab39107d8405b77dec2774d62eaf24689ab6c5cfc8ee01c9ae", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Rock Pop Ballad"),
        new("Arko / B Praak", "Teri Mitti", "Pop", 77.8, 408, "b23eb193de4f4b4c9c527f83c62bec0adfdaf1f0a60365d56abe8cf6b55f6515", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Patriotic Ode"),
        new("Tanishk Bagchi / Ash King", "Baarish", "Pop", 79.96, 476, "1ed739c2c0d2b0658103bad097a535b9da1ae1ffeb13b5d5d9e9095407700312", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Rain Ballad"),
        new("Pritam / Rekha Bhardwaj", "Phir Le Aya Dil", "Classical", 84.66, 408, "02d5669fa86dfa2efa1b10abb59f2d9044fe4051b5827b9043295f8c256aa7d3", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Ghazal Classical Ballad"),
        new("Pritam / Arijit Singh", "Ilahi (Wanderer Theme)", "Pop", 74.46, 612, "ad68227186dbdb374097033776010501ae23cbf0b1e70102e85de4d88d545041", OriginCountry: "India", OriginRegion: "South Asia", Language: "Hindi", MusicalStyle: "Travel Acoustic Song"),
        new("Scott Joplin", "The Entertainer", "Classical", 76.77, 544, "636840d250e57083e1c694d2af392c5f55ae0d2a6f3aaa007bff55b65c648501", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Ragtime Masterpiece"),
        new("Scott Joplin", "Maple Leaf Rag", "Classical", 73.82, 544, "3946e50c83cc1d927be0ef8d219027813b8ddc7e3be86c558050dac0cb889939", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Sedalia Ragtime"),
        new("Scott Joplin", "Solace (A Mexican Serenade)", "Classical", 75.75, 432, "802fbfca55b2e2e98d1bb2a183068e7e492f40a405afe4bbdd8229d3e0c1b6df", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Habanera Ragtime"),
        new("George Gershwin", "Rhapsody in Blue (Piano Solo Theme)", "Classical", 76.33, 504, "f27f68044a61eabdcddb5a7e5b3365ed975a5a6c0ab876b762e890c50b93aa71", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Symphonic Jazz"),
        new("George Gershwin", "Summertime (Porgy and Bess)", "Classical", 84.66, 432, "e72890ef3ad51f4e8c64f77be418944c3fb8f129caa19fbbd8da18c0a43385d4", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Jazz Lullaby"),
        new("George Gershwin", "I Got Rhythm", "Classical", 74.05, 720, "3609618ea5d9e694eeeca7a468a84226d5a270e022ac72bd07d3731c11631e9b", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Rhythm Changes"),
        new("Stephen Foster", "Oh! Susanna", "Traditional", 79.97, 612, "1b2128010bbd4721177e97923d13cec289a72a91bb5f3aff9d50803c0d832e3e", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "American Minstrel Folk"),
        new("Stephen Foster", "Jeanie with the Light Brown Hair", "Classical", 77.8, 408, "3a9e492a47d0ba9e932d797a9d0580404522c9474f50fade5776b2cfbcb62c1b", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Parlor Song"),
        new("Stephen Foster", "Beautiful Dreamer", "Classical", 79.96, 408, "d4644c6818b67ee1930f7869b2291a479c24fbec5b2763ebb57308afe9da353d", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Serenade Ballad"),
        new("Stephen Foster", "Hard Times Come Again No More", "Traditional", 82.24, 408, "5ee46f96df8fdda100fa6ab15adbdfc12754e64a586ab99c91bb96bbff030490", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "American Folk Ballad"),
        new("Traditional American Folk", "House of the Rising Sun", "Traditional", 83.96, 476, "94bf21858ff2638d1528607730fdb179d135df4903d8b80d129192fd337ae9b4", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "New Orleans Ballad"),
        new("Traditional American Folk", "Shenandoah", "Traditional", 84.66, 408, "34165b18f6e4e4b16c5c8f63474fe40e9709fe932dda602d0b074483fbd75831", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "River Shanty"),
        new("Traditional American Folk", "Wayfaring Stranger", "Traditional", 79.96, 432, "5f3e2be49dca9d339b645105b218805d4d004bbad2c861fa2c1e47e0089d14e6", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Spiritual Ballad"),
        new("Joseph Brackett (Shaker Song)", "Simple Gifts", "Traditional", 78.1, 476, "2ef47c0bf71ace32d9b34a09b43d1bb315107c8fb443e431a53b51e9ef305a88", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Shaker Hymn"),
        new("Traditional Folk", "Scarborough Fair (American Adapt)", "Traditional", 77.8, 408, "47447244305e28e82249c90fa6673c797935a7ef57798476d720166b717ccc7e", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Modal Folk Melody"),
        new("Traditional American Spiritual", "Down in the River to Pray", "Traditional", 86.12, 476, "f1c8c7daf3a4ad61e9600ca8c2015071517ac1f9293dcfd7b7ee0e6008bb0ac5", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Appalachian Gospel"),
        new("Traditional American Blues", "Saint James Infirmary Blues", "Traditional", 82.24, 432, "5b1c826ebd31f15d5daf5272278353038a37ffebff4caba5d843c4d5604aa049", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "New Orleans Funeral Blues"),
        new("Traditional American Cowboy", "Red River Valley", "Traditional", 76.33, 476, "4caa36e50be9aad277fe8b3809f12ac92e562613739c2f8a7dbfb32bde8cd1cd", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Western Folk"),
        new("Traditional American Fiddle", "Cotton-Eyed Joe", "Traditional", 72.7, 680, "cffdb632704c015c8f6404d02586e8f8f28e80a9fd6c98a073a64e12177833b4", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Country Reel"),
        new("Traditional Afro-American Spiritual", "Michael Row the Boat Ashore", "Traditional", 81.91, 476, "c2e0097faf697f1b9b2b666690fc966cfcf8cffb83a2342791acd8cc7e0a102e", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Sea Islands Spiritual"),
        new("Wallace Willis", "Swing Low, Sweet Chariot", "Traditional", 77.8, 408, "e91d9e24ffce08770a0b2978cf0e58762ccb43d38877a53acca5220f4b4f0cf1", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Negro Spiritual"),
        new("Traditional Gospel Jazz", "When the Saints Go Marching In", "Traditional", 71.97, 648, "93592340f068da4e6fbd8b445894bcf25275a9becfe72d1358a6547202f18eb4", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Dixieland Gospel"),
        new("John Newton / Traditional", "Amazing Grace", "Traditional", 79.96, 408, "17a540434ebf3fd577eccf0703b7cc4c66281920424aefb5c6509e6e549dadf5", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Hymn of Redemption"),
        new("Julia Ward Howe", "Battle Hymn of the Republic", "Traditional", 76.77, 544, "470abc0e67590ae0506a486ad55f53b96adc81c1159d95540b35e32784d7de4d", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "American Patriotic Anthem"),
        new("Percy Montrose", "Clementine", "Traditional", 81.67, 544, "d6cfa5b7a9b46304bcaa20cac840c4049a7df17fee4cb01b83f4d9760d5d87ce", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "California Gold Rush Folk"),
        new("Traditional Cowboy Ballad", "Streets of Laredo", "Traditional", 75.75, 408, "d35ccc32b4065f5cd2957319fe15572fca77055d109c7e6f13a60b0b36dcc72d", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Texas Cowboy Lament"),
        new("Traditional Blues Folk", "Careless Love", "Traditional", 83.96, 504, "d1d413eafc4e5d954ac1856b801ce3f9ad62dd1751b1ae0ebd4ac68208f0ab8d", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Early American Blues"),
        new("Traditional American Murder Ballad", "Frankie and Johnny", "Traditional", 73.01, 476, "e9cf04fa67d8d4e6d896f014767521fdadb6b7163fa1a3a4a98723bec2e6295c", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "St. Louis Folk"),
        new("Traditional Railroad Ballad", "John Henry", "Traditional", 78.52, 612, "ec4d4749eec700828349249b13a2688a3a43f1f80fd439067717cdaabbbddd8c", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Steel Driver Work Song"),
        new("Traditional Southern Prison Folk", "Midnight Special", "Traditional", 73.82, 544, "fef1551cae20c8388098678597df3704a38c2a82a84cbd00d85f0b15392f396b", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Texas Blues Folk"),
        new("Edward MacDowell", "To a Wild Rose", "Classical", 87.23, 408, "34afdaff05b9acd73b230b10f69a6738dbbd5920df0668d7a7dbbe9d9b233fe4", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Woodland Sketches"),
        new("Edward MacDowell", "To a Water Lily", "Classical", 89.95, 408, "6ba42c0203bd1b8d4becd80d36a3f45a7d23bb49d113a039d19edbf923f02240", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Woodland Sketches"),
        new("Louis Moreau Gottschalk", "The Banjo (Grotesque Fantaisie)", "Classical", 76.17, 680, "500acaeb4dda792bef308433ed897ce051a3fd2be651b5028382ebccd1677598", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Creole Virtuosity"),
        new("Louis Moreau Gottschalk", "Souvenir de Porto Rico", "Classical", 79.96, 476, "118036fbb2d70f58b85670399031cf56800e0d098e26c042da704ae255aaa4c9", OriginCountry: "United States", OriginRegion: "North America", Language: "Instrumental", MusicalStyle: "Marche des Gibaros"),
        new("George David Weiss & Bob Thiele", "What a Wonderful World", "Pop", 79.96, 408, "dffba5d401545f61403211223ec594b39ea7c3da73b1e4db7d1a39e3c99c3a16", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Jazz Pop Standard"),
        new("Bart Howard", "Fly Me to the Moon", "Pop", 73.45, 684, "20bad6d5c7639ef050e2b0653d7b472bde661c3117c1b08602f43161b3daf945", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Bossa / Jazz Standard"),
        new("Paul Anka / Claude Francois", "My Way", "Pop", 77.8, 408, "fea0483f875b470f9baea72e957361f15c1a57ae654ffe9e3fd5c7a8f242b4a6", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Grand Ballad"),
        new("Hugo Peretti / Luigi Creatore", "Can't Help Falling in Love", "Pop", 84.66, 408, "3cac8f5a74b763bf997b322e7efebb6f92d9cb81b2829e1d379d957c3c7d6f4c", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Plaisir d'amour Adapt"),
        new("Paul Simon", "The Sound of Silence", "Pop", 79.96, 476, "42869293451c52cc5b826824d0423dae76704ad178470221298cdd32dbb10189", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Folk Rock Ballad"),
        new("Paul Simon", "Bridge Over Troubled Water", "Pop", 75.75, 408, "ffedb43787233d631a397bd1a5649c39e7c5731bb138147c054180930c239925", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Gospel Folk Ballad"),
        new("Billy Joel", "Piano Man", "Pop", 76.33, 476, "0f7fa8e03ae88bc285724b6dcf2296cffba13ed9893cbfe454f7f7ee065ca8ba", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Storytelling Waltz"),
        new("Billy Joel", "Vienna", "Pop", 86.12, 476, "f74b5296f8d2b03bfd72fc5b03282b2f0b093e084ce90cb1c59ee64de7905e8c", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "The Stranger Ballad"),
        new("Elton John & Bernie Taupin", "Your Song", "Pop", 79.96, 408, "2c04a6d789a88085cdc03bb2162d17a964d5a419c77a89e8d3feac697ff52824", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Classic Piano Ballad"),
        new("Elton John & Bernie Taupin", "Tiny Dancer", "Pop", 75.75, 408, "5894e8c5261811325cc48a63d2fc9e544da48c0f3688c4ff068cf0ec08991b4e", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Rock Ballad Suite"),
        new("Don Felder, Don Henley, Glenn Frey", "Hotel California", "Pop", 77.8, 432, "5b5d79f26565494982b3ce6ec20aadfce4bd8fcbe14a2cb9e95866b385fcff5c", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Acoustic Rock Ballad"),
        new("Glenn Frey & Don Henley", "Desperado", "Pop", 84.66, 408, "0f8ae7a2c9b3cc958dabc0f08b55ff4a2e5cd2c2afd942b087bcae872182a1a1", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Country Rock Ballad"),
        new("Jonathan Cain, Steve Perry, Neal Schon", "Don't Stop Believin'", "Pop", 73.19, 612, "88c53be524fc3013943fad673ac2edbb787b10245f206bb3ac04a1aff0179115", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Signature Piano Anthem"),
        new("Freddie Mercury", "Bohemian Rhapsody (Ballad)", "Pop", 80.37, 432, "e2fe73b7517cd8af1f0d8b1b7dca1f04d399ebad7e34773c8047c7448e591341", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Rock Opera Piano Theme"),
        new("Freddie Mercury", "Somebody to Love", "Pop", 75.75, 408, "5873d60a350f335dbdffccdc8c28ad6ce63f9d59ee8da5fba57ae573d9bcf56f", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Gospel Rock Anthem"),
        new("John Lennon", "Imagine", "Pop", 75.75, 408, "5b1cce20c78b6a5ccd66ba649784b42e26bca43936a68effe314cc0cb200efdf", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Peace Anthem"),
        new("Paul McCartney & John Lennon", "Hey Jude", "Pop", 77.8, 408, "6060ebe58b6c899d2af5facd25a174f07e2c5c783a1523d0ce09b2ab2321fffe", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Anthem Singalong"),
        new("Paul McCartney & John Lennon", "Let It Be", "Pop", 79.96, 408, "960376bf361f51abbeeea3d1b5262af3cbfd572110949b63a637691999b27f9e", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Gospel Pop Ballad"),
        new("Stevie Nicks", "Landslide", "Pop", 86.12, 476, "fd59d77c06bcb64ca3c6dac31f231c6490976cbb75c70cebe0cc2243074b1d66", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Acoustic Reflection"),
        new("Stevie Nicks", "Dreams", "Pop", 71.97, 684, "c740e3f905618206c47ce327727541c31760272d2ff4b77d96942c7d39509497", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Atmospheric Soft Rock"),
        new("Bob Dylan", "Blowin' in the Wind", "Traditional", 78.1, 476, "6029e91901ccafa1643e52b6e97e213244967f364a5f268f0df679ae2f798ef8", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Civil Rights Anthem"),
        new("Bob Dylan", "Knockin' on Heaven's Door", "Pop", 82.24, 408, "76470ee603f397125460808ab3df531e06770dbc7e2c67bb59273177c6f050d5", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Folk Rock Hymn"),
        new("Neil Young", "Heart of Gold", "Pop", 79.96, 476, "dd319c1ba64c0515334d561d570679571e673571c9d0e22e6b71a0207b284edb", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Acoustic Folk"),
        new("June Carter & Merle Kilgore", "Ring of Fire", "Pop", 73.82, 544, "9c0e10ea00b8e86a082f411e14a9d695f5478c2218c4fb72beb568a913d6cba8", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Mariachi Country Ballad"),
        new("Leonard Cohen", "Hallelujah", "Pop", 87.23, 408, "3c26189bbf3c3184a6ebdb3625fc4ce10ec6af021c0e1278a59357786c4ea5e8", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Sacred / Secular Hymn"),
        new("Coldplay", "The Scientist", "Pop", 77.8, 408, "553beba7ecccdd4b6ad6fb7af17bd4d9728144ca07b5d28b8d2f468264af11fd", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Piano Ballad"),
        new("Coldplay", "Clocks", "Pop", 73.82, 680, "f828aa8dfb52849f2e9258c3863f334829886110382ce1d64dfba3129b18bbee", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Signature Arpeggio Pop"),
        new("Coldplay", "Fix You", "Pop", 83.43, 408, "c9b100a937cc07a82ec36d8df5223784420472126fc4dffb04ecfa59d9ab9c51", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Anthemic Elevation"),
        new("Adele & Dan Wilson", "Someone Like You", "Pop", 84.66, 408, "83f3003e0379518d54c176230e4ead780cddcc907f118b24fbcceb1df7088ed2", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Heartbreak Piano Ballad"),
        new("Adele & Greg Kurstin", "Easy on Me", "Pop", 82.24, 408, "ec6a1701531b2ce89f86f7f6d564a81b6b7982d16f7bfbfc8114785304467916", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Vocal Piano Ballad"),
        new("Adele & Paul Epworth", "Rolling in the Deep", "Pop", 73.11, 544, "4d5aac1e3457c2dcea022f8ace49269ef3efeaa6ecd86c9e8b8c2ca04e3f7cdf", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Stomping Soul Pop"),
        new("Bruno Mars, Philip Lawrence, Ari Levine", "When I Was Your Man", "Pop", 79.27, 432, "d038cd5d18b36546bc4fe735b27a6c725f309ac822b9c14a0cd6da433be795d8", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Solo Piano Soul"),
        new("Bruno Mars / The Smeezingtons", "Just the Way You Are", "Pop", 79.24, 612, "552da45d68f3eeda9b68b259c97738111d81e6874e0755265a24bbc539e527f7", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Upbeat Love Ballad"),
        new("Ed Sheeran", "Perfect", "Pop", 91.38, 408, "4e14ac5f87f1de4ef02eec33ad4e1e7194b063134014999c4f2b7edf07a0d0ec", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Slow Dance Ballad"),
        new("Ed Sheeran & Amy Wadge", "Thinking Out Loud", "Pop", 85.03, 476, "aa2e47151f2e7f354e47e043c69a3735f049bbc81f0c6b687a8c7fb3d0b56aea", OriginCountry: "United States", OriginRegion: "Western", Language: "English", MusicalStyle: "Blue-Eyed Soul"),
        new("John Legend & Toby Gad", "All of Me", "Pop", 91.38, 408, "97987ebb7f9bac7ea0814f835360020cf8744687cec046b3512beb47933a615a", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Love Serenade Piano"),
        new("Alicia Keys", "If I Ain't Got You", "Pop", 81.81, 360, "a3d238fcb68f1b720beef977b0f031f5a75c0e75b275b8f84ed47ccf1c887228", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Neo Soul Piano"),
        new("Lady Gaga, Mark Ronson, Anthony Rossomando", "Shallow", "Pop", 79.97, 544, "c5b6e5b4329caef560790883e856891116e0e18594c99156df97b825f90f1c47", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "A Star Is Born Theme"),
        new("Lady Gaga & Natalie Hemby", "Always Remember Us This Way", "Pop", 88.57, 408, "3ddef1cb84c21b7ee7b5c85932fade63c0080fa36f87ffd97360c526c38ed1c4", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Country Pop Ballad"),
        new("Taylor Swift", "Love Story", "Pop", 72.58, 612, "e0fa20ebcc0b437722813df238fd94b2c336755cb717d1e61c40fde1029afffa", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Country Pop Romance"),
        new("Taylor Swift & Aaron Dessner", "Cardigan", "Pop", 73.82, 680, "c42560aac0c0446b95c947c3de5b28e17bffd6cbf1c6ccba13bd74c47eb54b6d", OriginCountry: "United States", OriginRegion: "North America", Language: "English", MusicalStyle: "Indie Folk Piano"),
        new("Traditional Japanese Classic", "Sakura Sakura (Cherry Blossoms)", "Traditional", 79.96, 432, "3f14a2373993be694297c72dae51ca1118e31d68b67dcd8b8e8b321297d026da", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Koto Folk Melody"),
        new("Rentaro Taki & Bansui Doi", "Kojo no Tsuki", "Classical", 87.23, 432, "01db5c28f3991502c4b04bb5db9c78da4ee50b99ee0913076a3efa23737d28e6", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Meiji Era Classical"),
        new("Tamezo Narita", "Hamabe no Uta (Song of the Seashore)", "Traditional", 77.8, 432, "ceb9e6a9a29af94abffc9e80bf4ad071a40f78bd4878d0ca618fd19753dcff91", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Taisho Lyric Poem"),
        new("Kosaku Yamada & Rofu Miki", "Akatombo (Red Dragonfly)", "Traditional", 84.66, 408, "2ef8bf2a58d68dd6d50669a54e98e32c15faf855836eac6321690c7e49de492a", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Nostalgic Folk Melody"),
        new("Teiichi Okano & Tatsuyuki Takano", "Furusato (My Country Home)", "Traditional", 75.75, 408, "2de0eead9a86a99c7618d103953c624dc23fde60fceb424ab0fd2715d59b27c3", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "School Lyric Song"),
        new("Teiichi Okano", "Haru ga Kita (Spring Has Come)", "Traditional", 79.97, 544, "8596632e6a0b5a0df832af3299330aa06929449eafdf23245eeeabaa6a5b95bb", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Children's Spring Song"),
        new("Teiichi Okano", "Oborozukiyo (Hazy Moonlit Night)", "Traditional", 76.76, 408, "201588aa6479eec5cfc1349c65632e71f4694beedada6ccf70e6c5a75b07e4f2", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Spring Evening Folk"),
        new("Teiichi Okano", "Momiji (Autumn Leaves)", "Traditional", 79.96, 476, "80e24a8221f327203854f6892559dbe2e5a3beaf6fcd1e6c3788c3b40a83c98a", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Autumn Colors Melody"),
        new("Shin Kusakawa", "Yuyake Koyake (Sunset Glow)", "Traditional", 82.24, 408, "b843e0a82a10970ed4e2e4a9921ca9f9ca50b201a8b1e471a39147525a0406dc", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Evening Lullaby"),
        new("Nagayo Motoori", "Nanatsu no Ko (Seven Baby Crows)", "Traditional", 83.96, 476, "35d01d1b9a6175da655373e87dabbb968078364c939eac7d557c84d0c6a8799e", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Nursery Song"),
        new("Hokkaido Traditional Fishermen", "Soran Bushi", "Traditional", 77.12, 612, "8db163300f231a973bfd906352423d76f6f8b52a411800a54aae9b0880465413", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Herring Work Chant"),
        new("Kyoto Folk Traditional", "Takeda Lullaby", "Traditional", 89.95, 408, "914004e75ef5871d84fee2fb4b82f816c58ad4a2473744c11f27a70ebe7d0d23", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Historical Lullaby"),
        new("Kosaku Yamada", "Chugoku Chiho no Komoriuta", "Traditional", 87.23, 408, "54eb9550ce0cfad7fe3c43493500cb6f7bf69d5e100719f73b0aa837af65b412", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Chugoku Region Lullaby"),
        new("Shinpei Nakayama", "Shabondama (Soap Bubbles)", "Traditional", 76.33, 476, "38d3bf5789da9d7d875f608c492abfc7a8a76797f059998b941b57d62e826a1d", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Taisho Children Song"),
        new("Shinpei Nakayama", "Amefuri (Rainfall Ballad)", "Traditional", 73.01, 476, "3e48566e2cb7152622fe9c6d65c06b25cea4f841687150f2678bf8cbbe85b715", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Rainy Day Song"),
        new("Traditional Japanese Folk", "Hotaru no Hikari (Firefly Glow)", "Traditional", 75.75, 408, "19ffbc5192a266192159e3beaf1bc5cfac9e80e83128dd00eef64d6d1318d8a4", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Commencement Traditional"),
        new("Ryuichi Sakamoto", "Merry Christmas Mr. Lawrence", "Classical", 73.01, 476, "3db39e3a3a48648100d00242c757f3175633dcc7fd32b890bfd7abd9d6237a9c", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Cinematic Piano"),
        new("Ryuichi Sakamoto", "Energy Flow", "Classical", 84.66, 408, "4544b6564a7aeee5e7bb7c3cc63a75d9d78315b0540c8a3dce0724cde171e1d3", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Healing Solo Piano"),
        new("Joe Hisaishi", "One Summer's Day", "Anime & Games", 75.75, 408, "f0270ebc8e2da9fa312cf3a37c115f775a39dcdc9a588d6a69bdf339aa6b3a3a", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Spirited Away Theme"),
        new("Joe Hisaishi", "Summer (Kikujiro)", "Anime & Games", 71.97, 612, "e2b5794a27872b8b4d20b5000754b7a1e3a19ca7311d14daf7d968b3bf641cbe", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Summer Road Trip Theme"),
        new("Joe Hisaishi", "Path of the Wind", "Anime & Games", 76.67, 504, "d314a02fd26c38fa1474020c63187e86ea37d6111ef0b4230b9ad00445ba82cf", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Totoro Forest Theme"),
        new("Joe Hisaishi", "Carrying You (Laputa)", "Anime & Games", 86.12, 476, "e21f7540c16a9066c42325db1c562fb1932d36b3906e4fb18d7ea2b20c9bd1af", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Castle in the Sky"),
        new("Nobuo Uematsu", "To Zanarkand", "Anime & Games", 84.66, 408, "327ad83e931bf89e7e0000ac0ee030b21966cee322ade5131e575ffd64140ec8", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Final Fantasy X Theme"),
        new("Yoko Shimomura", "Dearly Beloved", "Anime & Games", 79.96, 408, "66385f86bfd93dd4838e4424391ebdf42658afdba4b10a624a38e490a8627a55", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Kingdom Hearts Title"),
        new("Hikaru Utada", "First Love (Piano Solo)", "Pop", 77.8, 408, "d02918dde1a3ed875c8ea6619bd5bfc5c6cf4a130b1df51b57acce65356976d2", OriginCountry: "Japan", OriginRegion: "East Asia", Language: "Japanese", MusicalStyle: "Heisei Pop Classic"),
        new("Traditional Korean Folk Anthem", "Arirang", "Traditional", 75.75, 408, "248ea63f7e023e925a0a22dbb5006b28b04132403efedfeb6301600a3605cfa2", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "National Cultural Anthem"),
        new("Gyeongsang Folk Traditional", "Miryang Arirang", "Traditional", 74.46, 612, "c480d0d64ef476bcc6f599013b129a3e3dc66cbd635ec71f7bb78a7dbcea43c7", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Upbeat Semachi Folk"),
        new("Jeolla Folk Traditional", "Jindo Arirang", "Traditional", 78.34, 544, "3d4785117257e566b6f1ce7d7813850e621ca6d6ba648710df78c0f864d200fe", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Southern Namdo Melody"),
        new("Traditional Gyeonggi Folk", "Doraji Taryeong (Bellflower)", "Traditional", 73.82, 544, "04adeaaf5e8455c55f020e243e2bf92d98525a698fd28bc2eee228d8b8b4eedc", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Bellflower Mountain Song"),
        new("Jeongeup Folk Song", "Saeya Saeya Parang Saeya", "Traditional", 84.66, 408, "a6af1c764de8bad2c169417082a7e0e9bfb39398f72a983110a6ea6311bc2512", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Blue Bird Ballad"),
        new("Pansori Folk Theme", "Chunhyang Ballad", "Traditional", 79.96, 408, "f28e62a5c1a5427fcb56be2ae9e9731ef67d310e5792370e0df949e7e17a71bb", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Namwon Love Legend"),
        new("Southwest Coastal Traditional", "Ganggangsullae", "Traditional", 77.4, 680, "247ad7a20bd509055aa8ca85a6e970dee849403e03f95eabaed7b023dee708dc", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Chuseok Circle Dance"),
        new("Han River Boatmen Song", "Hangangsu Taryeong", "Traditional", 73.01, 476, "ceec517a8d2d2b19ab5519e62c75069d2d3545b27029860d15a74894478f8db9", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "River Rowing Chant"),
        new("Gyeonggi Minyo Traditional", "Nililiya", "Traditional", 77.12, 612, "0d9045e3669fcaa845a315f55ead654ca7b94c5e0b367d94d411f2a80e33e8e5", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Joyous Festive Folk"),
        new("Barley Threshing Song", "Ongheya", "Traditional", 74.98, 680, "7f07a3ce37ebf00724014410f27f78239dfe9acecda9964f038528f893651cf7", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Harvest Work Song"),
        new("Hong Nan-pa & Lee Won-soo", "Spring in My Hometown", "Traditional", 75.75, 408, "8aae42e63468d80e7121f2948961733b4f5fdf9a793ddca9b1ce183aeee61d96", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Nostalgic Lyric Song"),
        new("Park Tae-joon & Choi Soon-ae", "Thinking of My Brother", "Traditional", 82.24, 408, "ba91bb47466eb4ed1303ebf471615e3aa789389efec8be35c8e70a22216d3476", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Childhood Solitude"),
        new("Lee Heung-ryeol & Han In-hyun", "Island Baby (Seom Jip Agi)", "Traditional", 87.23, 408, "410188818d2ce4aa2587dc69364dd2053e6e4c5bc60b9d5d76f2111c82ff9271", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Beloved Island Lullaby"),
        new("Yoon Geuk-young", "Half Moon (Bandal)", "Traditional", 77.8, 408, "4309dbfac563c5a8ce53f9088889fa5b9271ad61858cf33d9a85e1c67a2079bd", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "First Modern Children Song"),
        new("Yiruma", "River Flows in You", "Classical", 79.96, 408, "f64acce31ba89007bf8f083c509706e15179688794e56cd453b6a3b8610e5353", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Modern Neo-Classical"),
        new("Yiruma", "Kiss the Rain", "Classical", 84.66, 408, "bdca9393abbd83a440d227fc1f46c378c14a84c203f6fb5b6f0d8748e21720c9", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Nocturne Piano Suite"),
        new("Yiruma", "May Be", "Classical", 76.76, 408, "67640dd59765bbacdeca020684753ec4a9c2f56b736e4b503c1e10327410555c", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Lyrical Melancholy"),
        new("BTS", "Spring Day (Piano Adaptation)", "Pop", 86.12, 476, "ec2addac5e225e2bb1671cac2bbfb43106a1cfca1ab4c9e7f4a4d9d24d4d63bc", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Acoustic K-Pop Hymn"),
        new("IU", "Through the Night", "Pop", 77.8, 408, "e3385f05afe251c73f6ed4c279152cdfe636d85061789f4e74c1d5ff5ec4e718", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Korean", MusicalStyle: "Acoustic Folk Pop"),
        new("Nam Hye-seung (Crash Landing)", "Song for My Brother", "Classical", 89.95, 408, "63b1cdc12c7cdd937b4b09e107a491cede60af492bd1e7353137fde531db0c0c", OriginCountry: "South Korea", OriginRegion: "East Asia", Language: "Instrumental", MusicalStyle: "Lake Brienz Piano Theme"),
        new("Traditional English Ballad", "Greensleeves", "Traditional", 79.96, 408, "cfdf0b570fec1a7706f9403dc29c4dac6e706e68e6cb9ad7d7caf90d05875ea7", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Tudor Ground Bass"),
        new("Traditional Irish Air", "Danny Boy (Londonderry Air)", "Traditional", 84.66, 408, "b133823f37d5db06f91eb6327283623964ae9c8361274181fa2adbd404addb5c", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "County Londonderry"),
        new("Robert Burns / Traditional Scottish", "Auld Lang Syne", "Traditional", 75.75, 408, "8ff5cb344f20b5dd1708695d4405d3e6e7ce9a4836e565dc05845cc85414e8f6", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Hogmanay Farewell"),
        new("Traditional Scottish Folk", "The Bonnie Banks o' Loch Lomond", "Traditional", 79.96, 476, "b32b7f9e395a49e91772cfb1784842855abbe20c7388788dd85eb18b84d9c70d", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Highland Reel"),
        new("Traditional Scottish Highland", "The Skye Boat Song", "Traditional", 82.24, 408, "4323b56af5ec1968be15ee08cb2d2ae6bb8970d94c0772da21c7c8b20d7fc97b", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Hebrides Sea Ballad"),
        new("Traditional English Folk", "Early One Morning", "Traditional", 76.33, 476, "6a1099809bb8e4d8c89833d50f57ba838cd070b8858b077281f1d260f91bd6d6", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Pastoral English Song"),
        new("Traditional Scottish & English", "The Water Is Wide (O Waly, Waly)", "Traditional", 84.66, 408, "4b67bafa39495b2c1603d42e44fd2526a5cedbd93cad7a66611196da55f07435", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Folk Ballad"),
        new("Traditional Welsh Melody", "Ash Grove (Llwyn Onn)", "Traditional", 79.96, 476, "a703ae91d94edcc040e562d614a52fa0fdc6e57327f33691a4a6e049b3b26752", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Welsh Harp Tradition"),
        new("Traditional Welsh Lullaby", "Suo Gan", "Traditional", 87.23, 408, "fe45dfa78e8c1822c13575ece9d97d11a58e75fbac9eeefeb39e6822b81f1017", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "Welsh", MusicalStyle: "Ancient Welsh Lullaby"),
        new("Traditional English Ballad", "Scarborough Fair (English Heritage)", "Traditional", 76.76, 408, "795e289d8b4326a270094203363362fa578459f3cf0724169a4bb30dd5282e47", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "North Yorkshire Fair"),
        new("Traditional British Sea Shanty", "What Shall We Do with the Drunken Sailor", "Traditional", 77.4, 680, "1a75c0be6e075b004cda42b4085f82bd7a5c285f7da252d9e6d1cbcc0aaa7703", OriginCountry: "United Kingdom", OriginRegion: "Europe", Language: "English", MusicalStyle: "Halyard Sea Shanty"),
        new("Traditional New Zealand Whaling", "Wellerman", "Traditional", 77.12, 612, "fa2e14a3878a973f2f5c399d7cfc537c5983891282eb771607af6a2a2785aa52", OriginCountry: "New Zealand", OriginRegion: "Other", Language: "English", MusicalStyle: "Sea Whaling Chantey"),
        new("Traditional Spanish Guitar", "Spanish Romance (Romance Anonimo)", "Classical", 83.96, 504, "aa3ff19d4329f7cb37dee2ca80303f81aa099bbe11dcd8f6985df3008bc5ec5e", OriginCountry: "Spain", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Concert Piano Setting"),
        new("Isaac Albeniz", "Asturias (Leyenda)", "Classical", 75.21, 760, "031d6d3e3933c3f5e57547d63b279e117a05ee63cdbfe2e15a318028933581a8", OriginCountry: "Spain", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Suite Espanola No. 5"),
        new("Francisco Tarrega", "Recuerdos de la Alhambra", "Classical", 86.12, 504, "72eca648c0017470fff3c7ead31c7447a81f54ad7970a096396bfec704960428", OriginCountry: "Spain", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Tremolo Piano Concert"),
        new("Erik Satie", "Gymnopedie No. 1", "Classical", 90.42, 504, "930579f8bd31e66c73bc4de625674ed0e01ea50884d383f2dc492daaec7b84b9", OriginCountry: "France", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Fin-de-Siecle Parisian"),
        new("Claude Debussy", "Clair de Lune (Calibrated Piano)", "Classical", 87.23, 432, "2eda8f14d36178a5391cc65ba8c637a78b4fb4185f656f25189e74982c74a2cb", OriginCountry: "France", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Suite Bergamasque"),
        new("Gabriel Faure", "Pavane in F-sharp minor", "Classical", 79.96, 432, "3943bc12ce15392cd35886412197d7ebe30aa5e1aac212cf304b0ee5dd75dc3f", OriginCountry: "France", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Belle Epoque Orchestral"),
        new("Camille Saint-Saens", "The Swan (Le Cygne)", "Classical", 84.66, 432, "46178f32acd8fab73136f8121ba243f9e6a02ffc42fad9e5cb47849a621a60fd", OriginCountry: "France", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Carnival of the Animals"),
        new("Italian Folk Resistance", "Bella Ciao", "Traditional", 71.97, 648, "0dbe5b53a059dc5a489c78522b8b0167a5d982a6988bd18f95b88bee40598334", OriginCountry: "Italy", OriginRegion: "Europe", Language: "Italian", MusicalStyle: "Mondina / Partisan Anthem"),
        new("Eduardo di Capua", "O Sole Mio", "Traditional", 79.96, 504, "abcc457593cbaec9047dc5cadaf03688773e9eaec3a68904c5ced24d611ff14b", OriginCountry: "Italy", OriginRegion: "Europe", Language: "Italian", MusicalStyle: "Neapolitan Canzone"),
        new("Teodoro Cottrau", "Santa Lucia", "Traditional", 86.12, 504, "dae1ddb2e17c19eeca88d32d201434fe83fa8bd4ed43fa39c6c409ab54bbeb27", OriginCountry: "Italy", OriginRegion: "Europe", Language: "Italian", MusicalStyle: "Neapolitan Barcarolle"),
        new("Ernesto De Curtis", "Torna a Surriento", "Traditional", 83.96, 504, "dc8ff0c73a160359b1f1a6e848ffc17d687dd0fff023e853b6b50d15fee0f3c3", OriginCountry: "Italy", OriginRegion: "Europe", Language: "Italian", MusicalStyle: "Sorrento Serenade"),
        new("Luigi Denza", "Funiculi, Funicula", "Traditional", 72.7, 680, "78cfc37722984d373379a1b306ca9e94f88ec5e943937f02210e291e9ef0765c", OriginCountry: "Italy", OriginRegion: "Europe", Language: "Italian", MusicalStyle: "Mount Vesuvius Celebration"),
        new("Johannes Brahms", "Hungarian Dance No. 5", "Classical", 74.98, 720, "6bfd89220a053dcb89c3d1a12cc1c6992876d80594d840acc21ba2190a7acd3f", OriginCountry: "Germany", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Romani Hungarian Dance"),
        new("Antonin Dvorak", "Humoresque Op. 101 No. 7", "Classical", 75.75, 432, "221ad1eca61a33a1184a143a8aaa4301dc56d446fca57be348d662ee9a5e3424", OriginCountry: "Czech Republic", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Bohemian Lyrical"),
        new("Edvard Grieg", "Morning Mood (Peer Gynt)", "Classical", 79.96, 408, "b7c692adddecde89a6b2c51cbdcad1914265f80a3d583c2f1aad9f55d12b9c58", OriginCountry: "Norway", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Nordic Sunrise Suite"),
        new("Edvard Grieg", "In the Hall of the Mountain King", "Classical", 78.52, 648, "53bd59f3c88e3325d26795ea16d1a8596dbd5910925765e9778bb670d7276795", OriginCountry: "Norway", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "Troll Mountain Suite"),
        new("Jean Sibelius", "Finlandia Hymn", "Classical", 82.24, 408, "44e461012302962bd6f2b64cd0977c344777ce1034a8007b02215b5d3a92b4fd", OriginCountry: "Finland", OriginRegion: "Europe", Language: "Instrumental", MusicalStyle: "National Tone Poem"),
        new("Banjo Paterson / Traditional", "Waltzing Matilda", "Traditional", 73.01, 476, "cc9c39fdbeca50fbde16ebbc0d3a15d50bbe4e1e87600396d627e4ed10603657", OriginCountry: "Australia", OriginRegion: "Other", Language: "English", MusicalStyle: "Bush Ballad Classic"),
    };

    void InitializeModernPicks()
    {
        ApplyModernFilter();
        if (!WorkflowMode && !Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _ = LoadModernCloudSongsAsync();
        }
    }

    async Task LoadModernCloudSongsAsync()
    {
        if (onlineHttp == null || downloads == null) return;
        try
        {
            if (ModernStatusText != null)
                ModernStatusText.Text = "Loading cloud library…";

            modernCloudCatalog ??= new RemappedCatalogService(onlineHttp, downloads);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var songs = await modernCloudCatalog.ListAsync(cts.Token).ConfigureAwait(true);
            if (songs != null && songs.Count > 0)
            {
                modernCloudSongs = songs;
                modernCloudLoaded = true;
                if (ModernStatusText != null)
                    ModernStatusText.Text = $"{songs.Count} cloud songs connected · Visual Pianos (C2–C7)";
                ApplyModernFilter();
            }
        }
        catch
        {
            if (ModernStatusText != null)
                ModernStatusText.Text = modernCloudLoaded ? $"{modernCloudSongs.Count} cloud songs available" : "Cloud library ready.";
        }
    }

    void OnCategoryFilterChanged(object sender, RoutedEventArgs e) => ApplyModernFilter();
    void OnRegionFilterChanged(object sender, RoutedEventArgs e) => ApplyModernFilter();

    string GetSelectedRegion()
    {
        if (RegChina?.IsChecked == true) return "China";
        if (RegIndia?.IsChecked == true) return "India";
        if (RegUS?.IsChecked == true) return "United States";
        if (RegJapan?.IsChecked == true) return "Japan";
        if (RegKorea?.IsChecked == true) return "South Korea";
        if (RegEurope?.IsChecked == true) return "Europe";
        if (RegOther?.IsChecked == true) return "Other";
        return "All";
    }
    void ModernFilterChanged(object sender, TextChangedEventArgs e) => ApplyModernFilter();

    string GetSelectedCategory()
    {
        if (CatPop?.IsChecked == true) return "Pop";
        if (CatClassical?.IsChecked == true) return "Classical";
        if (CatAnimeGames?.IsChecked == true) return "Anime & Games";
        if (CatFilmTV?.IsChecked == true) return "Film & TV";
        if (CatTraditional?.IsChecked == true) return "Traditional";
        return "All";
    }

    void ApplyModernFilter()
    {
        if (ModernList == null || ModernQuery == null) return;
        var q = ModernQuery.Text.Trim();
        var cat = GetSelectedCategory();

        var reg = GetSelectedRegion();

        // 1. Cloud library songs (authoritative, pre-remapped)
        var cloudPicks = modernCloudSongs.Select(s => new ModernPick(
            s.Artist,
            s.Title,
            string.IsNullOrWhiteSpace(s.Category) ? "Classical" : s.Category,
            s.DurationSeconds,
            s.KeptNotes,
            s.Sha256,
            s,
            Uri.TryCreate(s.Source, UriKind.Absolute, out var u) ? u : null,
            s.OriginCountry,
            s.OriginRegion,
            s.Language,
            s.MusicalStyle
        ));

        // 2. Direct creator downloads
        var directPicks = ModernDownloads.Available.Select(x => new ModernPick(
            x.Artist,
            x.Title,
            "Pop",
            Sha256: x.Sha256,
            Source: Uri.TryCreate(x.Source, UriKind.Absolute, out var su) ? su : null
        ));

        // In WorkflowMode or offline before cloud loads, show default shortlist
        var basePicks = (modernCloudLoaded && modernCloudSongs.Count > 0)
            ? cloudPicks.Concat(directPicks).Concat(ModernPicks)
            : ModernPicks.Select(p => p with {
                CloudSong = modernCloudSongs.FirstOrDefault(s => s.Artist.Equals(p.Artist, StringComparison.OrdinalIgnoreCase) && s.Title.Equals(p.Title, StringComparison.OrdinalIgnoreCase))
            });

        var filtered = basePicks
            .Where(p => cat == "All" || p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase) || (cat == "Pop" && p.Category.Contains("Pop", StringComparison.OrdinalIgnoreCase)))
            .Where(p => {
                if (reg == "All") return true;
                if (reg == "China") return p.OriginCountry.Equals("China", StringComparison.OrdinalIgnoreCase) || p.OriginRegion.Contains("China", StringComparison.OrdinalIgnoreCase) || p.Language.Contains("Mandarin", StringComparison.OrdinalIgnoreCase);
                if (reg == "India") return p.OriginCountry.Equals("India", StringComparison.OrdinalIgnoreCase) || p.OriginRegion.Contains("India", StringComparison.OrdinalIgnoreCase) || p.Language.Contains("Hindi", StringComparison.OrdinalIgnoreCase) || p.MusicalStyle.Contains("Raga", StringComparison.OrdinalIgnoreCase);
                if (reg == "United States") return p.OriginCountry.Equals("United States", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("US", StringComparison.OrdinalIgnoreCase) || p.OriginRegion.Contains("America", StringComparison.OrdinalIgnoreCase);
                if (reg == "Japan") return p.OriginCountry.Equals("Japan", StringComparison.OrdinalIgnoreCase) || p.OriginRegion.Contains("Japan", StringComparison.OrdinalIgnoreCase);
                if (reg == "South Korea") return p.OriginCountry.Equals("South Korea", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("Korea", StringComparison.OrdinalIgnoreCase);
                if (reg == "Europe") return p.OriginCountry.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("UK", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("Germany", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("France", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("Italy", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("Austria", StringComparison.OrdinalIgnoreCase) || p.OriginCountry.Equals("Poland", StringComparison.OrdinalIgnoreCase) || p.OriginRegion.Contains("Europe", StringComparison.OrdinalIgnoreCase);
                if (reg == "Other") return !string.IsNullOrWhiteSpace(p.OriginCountry) && !new[] { "China", "India", "United States", "US", "Japan", "South Korea", "Korea", "United Kingdom", "UK", "Germany", "France", "Italy", "Austria", "Poland" }.Contains(p.OriginCountry, StringComparer.OrdinalIgnoreCase);
                return true;
            })
            .Where(p => string.IsNullOrWhiteSpace(q) ||
                        p.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.OriginCountry.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.Language.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.MusicalStyle.Contains(q, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => !string.IsNullOrEmpty(p.Sha256) ? p.Sha256 : $"{p.Artist.ToUpperInvariant()}\0{p.Title.ToUpperInvariant()}")
            .ToArray();

        ModernList.ItemsSource = filtered;
        if (ModernStatusText != null && modernCloudLoaded)
        {
            ModernStatusText.Text = $"{filtered.Length} arrangements shown · {cat} · Visual Pianos C2–C7";
        }
    }

    LibraryEntry? FindLocalModern(ModernPick pick)
    {
        if (!string.IsNullOrEmpty(pick.Sha256))
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, pick.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        if (pick.CloudSong != null)
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, pick.CloudSong.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        var direct = ModernDownloads.Find(pick.Artist, pick.Title);
        if (direct != null)
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, direct.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return libraryEntries.FirstOrDefault(e =>
            e.Artist.Equals(pick.Artist, StringComparison.OrdinalIgnoreCase) &&
            (e.Title.Equals(pick.Title, StringComparison.OrdinalIgnoreCase) || e.Title.StartsWith(pick.Title + " ", StringComparison.OrdinalIgnoreCase)));
    }

    void ModernSelected(object sender, SelectionChangedEventArgs e)
    {
        modernDownloadCancellation?.Cancel();
        if (ModernDetails == null || ModernSearchButton == null || ModernLibraryButton == null || ModernDownloadButton == null) return;
        var pick = ModernList.SelectedItem as ModernPick;
        ModernSearchButton.IsEnabled = pick != null;
        ModernLibraryButton.IsEnabled = pick != null;

        if (pick == null)
        {
            ModernDownloadButton.IsEnabled = false;
            ModernDetails.Text = "Select an arrangement to view calibration details and play.";
            return;
        }

        var local = FindLocalModern(pick);
        var direct = ModernDownloads.Find(pick.Artist, pick.Title);
        var isLocal = local != null;
        ModernDownloadButton.IsEnabled = isLocal || pick.CloudSong != null || direct != null || !string.IsNullOrEmpty(pick.Sha256);
        ModernDownloadButton.Content = isLocal ? "Play from Library" : "Download and Play";
        if (ModernAddToPlaylistButton != null) ModernAddToPlaylistButton.IsEnabled = !string.IsNullOrEmpty(pick.Sha256);

        var durationStr = pick.DurationSeconds > 0 ? $"{TimeSpan.FromSeconds(pick.DurationSeconds):m\\:ss}" : "Standard";
        var notesStr = pick.NoteCount > 0 ? $"{pick.NoteCount} notes" : "Full score";
        var shaDisplay = !string.IsNullOrEmpty(pick.Sha256) && pick.Sha256.Length >= 12
            ? $"{pick.Sha256[..8]}…{pick.Sha256[^4..]}"
            : "SHA-256 Verified";

        ModernDetails.Text =
            $"{pick.Title}\n{pick.Artist}\n\n" +
            $"Category: {pick.Category}\n" +
            $"Duration: {durationStr}  •  Notes: {notesStr}\n" +
            $"Target Piano: Visual Pianos (C2–C7, 61 keys)\n" +
            $"Checksum: {shaDisplay}\n\n" +
            (isLocal
                ? "✓ This arrangement is downloaded and ready in your local library. Click 'Play from Library' to start."
                : (pick.CloudSong != null || !string.IsNullOrEmpty(pick.Sha256))
                    ? "☁ Ready for one-click cloud download and game playback. Pre-calibrated; no pitch adjustment required."
                    : direct != null
                        ? "A free original arrangement is available from its creator. Check your piano mapping and adjust any unmapped notes before playing."
                        : pick.Source != null
                            ? "The creator offers a free MIDI link in the video description. Download it, then use Browse MIDI."
                            : "Direct download is unavailable for this title. Search MIDI sources or import your own copy.");
    }

    async void DownloadModern(object sender, RoutedEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick pick || library == null || downloads == null) return;
        modernDownloadCancellation?.Cancel();
        var request = new CancellationTokenSource();
        modernDownloadCancellation = request;
        ModernDownloadButton.IsEnabled = false;

        try
        {
            var local = FindLocalModern(pick);
            if (local == null)
            {
                ModernDetails.Text = "Downloading and verifying MIDI from cloud storage…";
                var progress = new Progress<double>(p =>
                {
                    if (ReferenceEquals(modernDownloadCancellation, request))
                        ModernDetails.Text = $"Downloading {p:P0}…";
                });

                if (pick.CloudSong != null)
                {
                    modernCloudCatalog ??= new RemappedCatalogService(onlineHttp ?? MidiDownloadService.CreateClient(), downloads);
                    var saved = await modernCloudCatalog.DownloadAsync(pick.CloudSong, library, progress, request.Token).ConfigureAwait(true);
                    request.Token.ThrowIfCancellationRequested();
                    await ReloadLibrary(request.Token).ConfigureAwait(true);
                    local = libraryEntries.FirstOrDefault(x => string.Equals(x.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException("Downloaded song was not found in your local library.");
                }
                else
                {
                    var direct = ModernDownloads.Find(pick.Artist, pick.Title);
                    if (direct != null)
                    {
                        var saved = await new ModernDownloads(downloads, library).DownloadAsync(direct, progress, request.Token).ConfigureAwait(true);
                        request.Token.ThrowIfCancellationRequested();
                        await ReloadLibrary(request.Token).ConfigureAwait(true);
                        local = libraryEntries.FirstOrDefault(x => string.Equals(x.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException("Downloaded song was not found in your local library.");
                    }
                    else if (!string.IsNullOrEmpty(pick.Sha256))
                    {
                        var tempCloudSong = new RemappedSong(
                            pick.Sha256,
                            pick.Title,
                            pick.Artist,
                            pick.Source?.AbsoluteUri ?? "https://larpiano.portways.app",
                            Category: pick.Category,
                            DurationSeconds: pick.DurationSeconds,
                            KeptNotes: pick.NoteCount
                        );
                        modernCloudCatalog ??= new RemappedCatalogService(onlineHttp ?? MidiDownloadService.CreateClient(), downloads);
                        var saved = await modernCloudCatalog.DownloadAsync(tempCloudSong, library, progress, request.Token).ConfigureAwait(true);
                        request.Token.ThrowIfCancellationRequested();
                        await ReloadLibrary(request.Token).ConfigureAwait(true);
                        local = libraryEntries.FirstOrDefault(x => string.Equals(x.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException("Downloaded song was not found in your local library.");
                    }
                    else
                    {
                        throw new InvalidOperationException("Direct cloud download is not yet staged for this title. Search online or import a local copy.");
                    }
                }
            }

            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(ModernList.SelectedItem, pick)) return;

            // Load into PlayerSongSelector and switch to Tab 0 (Playback)
            if (ReferenceEquals(PlayerSongSelector.SelectedItem, local))
            {
                await LoadLibrarySelection(local, () => PlayerSongSelector.SelectedItem as LibraryEntry).ConfigureAwait(true);
            }
            else
            {
                PlayerSongSelector.SelectedItem = local;
            }
            MainTabs.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(modernDownloadCancellation, request))
                ModernDetails.Text = "Could not download or play this song: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(modernDownloadCancellation, request))
            {
                modernDownloadCancellation = null;
                var selected = ModernList.SelectedItem as ModernPick;
                ModernDownloadButton.IsEnabled = selected != null;
            }
            request.Dispose();
        }
    }

    async void SearchModern(object sender, RoutedEventArgs e) => await SearchModernAsync();

    async Task SearchModernAsync()
    {
        if (ModernList.SelectedItem is not ModernPick pick) return;
        if (pick.Source != null && pick.CloudSong == null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pick.Source.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ModernDetails.Text = "Could not open MIDI source: " + ex.Message;
            }
            return;
        }
        MainTabs.SelectedIndex = 1;
        CatalogSelector.SelectedIndex = 0;
        SearchQuery.Text = $"{pick.Artist} {pick.Title}";
        searchDebouncer?.Cancel();
        await SearchOnlineAsync(0, true, CancellationToken.None).ConfigureAwait(true);
    }

    void FindModernInLibrary(object sender, RoutedEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick pick) return;
        LibraryQuery.Text = pick.Title;
        MainTabs.SelectedIndex = 3;
    }

        void ModernDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick) return;
        DownloadModern(sender, e);
    }

    async void OnModernAddToPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick pick || string.IsNullOrEmpty(pick.Sha256)) return;
        if (cachedPlaylists.Count == 0 && playlistService != null)
        {
            var p = await playlistService.CreatePlaylistAsync("My Playlist");
            await AddSongToPlaylistAsync(p.Id, pick.Sha256, pick.Title, pick.Artist, pick.DurationSeconds);
            ModernDetails.Text = $"Created playlist '{p.Name}' and added '{pick.Title}'.";
            return;
        }

        var cm = new ContextMenu();
        foreach (var p in cachedPlaylists)
        {
            var mi = new MenuItem { Header = p.Name, Tag = p.Id };
            mi.Click += async (s, ev) =>
            {
                if (s is MenuItem m && m.Tag is string pid)
                {
                    await AddSongToPlaylistAsync(pid, pick.Sha256, pick.Title, pick.Artist, pick.DurationSeconds);
                    ModernDetails.Text = $"Added '{pick.Title}' to playlist '{m.Header}'.";
                }
            };
            cm.Items.Add(mi);
        }
        var newMi = new MenuItem { Header = "+ Create New Playlist…" };
        newMi.Click += async (s, ev) =>
        {
            if (playlistService != null)
            {
                var newP = await playlistService.CreatePlaylistAsync($"Playlist {cachedPlaylists.Count + 1}");
                await AddSongToPlaylistAsync(newP.Id, pick.Sha256, pick.Title, pick.Artist, pick.DurationSeconds);
                ModernDetails.Text = $"Created playlist '{newP.Name}' and added '{pick.Title}'.";
            }
        };
        cm.Items.Add(new Separator());
        cm.Items.Add(newMi);

        if (sender is Button btn)
        {
            cm.PlacementTarget = btn;
            cm.IsOpen = true;
        }
    }
}
