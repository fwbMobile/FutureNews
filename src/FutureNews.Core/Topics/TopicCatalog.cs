using FutureNews.Core.Models;

namespace FutureNews.Core.Topics;

/// <summary>
/// The built-in library of science-fiction inspired technology domains.
/// Keywords are matched against titles/abstracts; arXiv categories, scholarly search
/// phrases and RSS feeds are attached per domain so every topic gets both papers and news.
/// </summary>
public static class TopicCatalog
{
    private const double Core = 3.0;
    private const double Strong = 1.6;
    private const double Support = 0.9;

    public static readonly IReadOnlyList<Topic> All = new List<Topic>
    {
        new Topic
        {
            Id = "fusion",
            Name = "可控核聚变与未来能源",
            NameEn = "Fusion & Future Energy",
            Description = "磁约束/惯性约束聚变、超导磁体、裂变与储能等清洁能源的工程进展",
            Color = "#FF7A45",
            Keywords = new[]
            {
                ("可控核聚变", Core), ("核聚变", Core), ("nuclear fusion", Core), ("tokamak", Core),
                ("托卡马克", Strong), ("ITER", Strong), ("惯性约束", Strong), ("inertial confinement", Strong),
                ("激光聚变", Strong), ("磁约束", Strong), ("stellarator", Strong), ("仿星器", Strong),
                ("超导磁体", Strong), ("高温超导", Strong), ("high-temperature superconductor", Strong),
                ("磁流体", Strong), ("聚变堆", Strong), ("fusion reactor", Strong), ("聚变能", Strong),
                ("fusion energy", Strong), ("fusion device", Strong), ("fusion experiment", Strong),
                ("fusion material", Strong), ("fusion plasma", Strong), ("fusion field", Strong),
                ("fusion", Support),
                ("小型模块化反应堆", Strong), ("SMR", Strong), ("裂变", Support), ("核废料", Support),
                ("聚变中子源", Support), ("超导", Support), ("储能", Support), ("储能电池", Support),
                ("solid-state battery", Support), ("核聚变实验", Support), ("disruption", Support),
                ("D-T plasma", Support), ("钨偏滤器", Support), ("neutron blanket", Support),
                ("neutron source", Support), ("quench", Support), ("divertor", Support),
            },
            ArxivCategories = new[] { "physics.plasm-ph", "hep-ex", "nucl-ex", "physics.acc-ph", "physics.ins-det" },
            SearchQueries = new[]
            {
                "nuclear fusion tokamak plasma",
                "high-temperature superconductivity magnet",
                "nuclear fission reactor advanced",
                "grid energy storage battery",
            },
            Feeds = new[]
            {
                ("https://www.sciencedaily.com/rss/matter_energy/nuclear_energy.xml", "ScienceDaily 核能"),
                ("https://www.sciencedaily.com/rss/matter_energy/energy_technology.xml", "ScienceDaily 能源技术"),
                ("https://phys.org/rss-feed/physics-news/", "Phys.org 物理"),
                ("https://spectrum.ieee.org/feeds/topic/energy.rss", "IEEE Spectrum 能源"),
                ("https://www.nature.com/nature.rss", "Nature"),
                ("https://www.qbitai.com/feed", "量子位"),
            },
            NegativeKeywords = new[]
            {
                "call for papers", "conference call", "workshop announcement", "sponsored",
                // "fusion" in the signal-processing / multi-criteria sense
                "image fusion", "data fusion", "sensor fusion", "information fusion",
                "feature fusion", "multi-modal fusion", "TOPSIS",
            },

            // "reactor", "plasma" and bare "fusion" also occur far outside fusion research.
            AmbiguousKeywords = new[] { "reactor", "plasma", "fusion" },
        },

        new Topic
        {
            Id = "robotics",
            Name = "机器人与具身智能",
            NameEn = "Robotics & Embodied AI",
            Description = "人形机器人、灵巧手、具身智能、运动控制、自动驾驶与群体机器人",
            Color = "#4F8CFF",
            Keywords = new[]
            {
                ("人形机器人", Core), ("humanoid robot", Core), ("机器人", Core), ("robot", Core),
                ("具身智能", Core), ("embodied AI", Core), ("embodied agent", Core),
                ("灵巧手", Core), ("dexterous hand", Core), ("机械臂", Strong), ("robotic arm", Strong),
                ("运动控制", Strong), ("locomotion", Strong), ("运动规划", Strong), ("motion planning", Strong),
                ("强化学习", Strong), ("reinforcement learning", Strong), ("SLAM", Strong),
                ("同时定位与地图构建", Strong), ("自主导航", Strong), ("autonomous navigation", Strong),
                ("人机协作", Strong), ("collaborative robot", Strong), ("群体机器人", Strong),
                ("swarm robotics", Strong), ("无人机", Strong), ("drone", Strong),
                ("自动驾驶", Strong), ("autonomous driving", Strong), ("数字孪生", Support),
                ("仿真到现实", Support), ("sim-to-real", Support), ("VLA", Support),
                ("视觉语言动作", Support), ("teleoperation", Support), ("exoskeleton", Support),
                ("外骨骼", Support), ("bipedal", Support), ("legged robot", Support),
            },
            ArxivCategories = new[] { "cs.RO", "cs.AI", "cs.CV", "cs.LG", "cs.SY", "eess.SP" },
            SearchQueries = new[]
            {
                "humanoid robot manipulation learning",
                "dexterous hand robot",
                "embodied AI navigation",
                "autonomous driving robot learning",
            },
            Feeds = new[]
            {
                ("https://www.sciencedaily.com/rss/computers_math/robotics.xml", "ScienceDaily 机器人"),
                ("https://www.sciencedaily.com/rss/matter_energy/robotics.xml", "ScienceDaily 机器人工程"),
                ("https://phys.org/rss-feed/technology-news/robotics/", "Phys.org 机器人"),
                ("https://phys.org/rss-feed/technology-news/machine-learning-ai/", "Phys.org 机器学习"),
                ("https://spectrum.ieee.org/feeds/topic/robotics.rss", "IEEE Spectrum 机器人"),
                ("https://spectrum.ieee.org/feeds/topic/artificial-intelligence.rss", "IEEE Spectrum AI"),
                ("https://techcrunch.com/feed/", "TechCrunch"),
                ("https://www.qbitai.com/feed", "量子位"),
            },
            NegativeKeywords = new[]
            {
                "call for papers", "sponsored content", "best deals", "discount",
                // surgical robotics is a different field from embodied AI
                "robot-assisted", "robotic surgery", "laparoscopic", "prostatectomy",
                "cystectomy", "nephrectomy", "uteroplasty", "patency", "resection",
                "operative", "randomized", "clinical trial", "patient",
            },
        },

        new Topic
        {
            Id = "bio",
            Name = "人造子宫与生物工程",
            NameEn = "Synthetic Womb & Bioengineering",
            Description = "人造子宫、胚胎与器官培养、类器官、3D 生物打印、基因编辑与合成生物学",
            Color = "#3FD18B",
            Keywords = new[]
            {
                ("人造子宫", Core), ("人工子宫", Core), ("artificial womb", Core), ("synthetic womb", Core),
                ("ex vivo embryo", Core), ("宫腔外培养", Core), ("embryo culture", Core),
                ("胚胎培养", Core), ("体外胚胎", Strong), ("organoid", Core), ("类器官", Core),
                ("器官再生", Core), ("organ regeneration", Strong), ("3D bioprinting", Core),
                ("3D 生物打印", Core), ("合成生物学", Core), ("synthetic biology", Core),
                ("基因编辑", Core), ("gene editing", Core), ("CRISPR", Strong),
                ("基因治疗", Strong), ("gene therapy", Strong), ("干细胞", Strong), ("stem cell", Strong),
                ("iPSC", Strong), ("induced pluripotent", Strong), ("器官芯片", Strong),
                ("organ-on-chip", Strong), ("胎盘模型", Strong), ("placenta model", Strong),
                ("胎儿发育", Strong), ("fetal development", Strong), ("围产期", Strong),
                ("perinatal", Support), ("neonatal", Support), ("single-cell", Support),
                ("单细胞测序", Support), ("tissue engineering", Support), ("组织工程", Support),
                ("bioreactor", Support), ("microphysiological", Support),
            },
            ArxivCategories = new[] { "q-bio.PE", "q-bio.QM", "q-bio.GN", "q-bio.MN", "q-bio.TO", "q-bio.CB", "q-bio.BM" },
            SearchQueries = new[]
            {
                "artificial womb ex vivo embryo culture",
                "organoid organ regeneration tissue engineering",
                "CRISPR gene editing synthetic biology",
                "stem cell differentiation developmental",
            },
            Feeds = new[]
            {
                ("https://www.sciencedaily.com/rss/health_medicine/pregnancy_and_childbirth.xml", "ScienceDaily 围产期"),
                ("https://www.sciencedaily.com/rss/health_medicine/stem_cells.xml", "ScienceDaily 干细胞"),
                ("https://www.sciencedaily.com/rss/health_medicine/gene_therapy.xml", "ScienceDaily 基因治疗"),
                ("https://www.sciencedaily.com/rss/health_medicine/genes.xml", "ScienceDaily 基因"),
                ("https://www.sciencedaily.com/rss/health_medicine/fertility.xml", "ScienceDaily 生育"),
                ("https://www.sciencedaily.com/rss/computers_math/computational_biology.xml", "ScienceDaily 计算生物学"),
                ("https://phys.org/rss-feed/biology-news/", "Phys.org 生物"),
                ("https://www.nature.com/nature.rss", "Nature"),
                ("https://www.science.org/rss/news_current.xml", "Science 新闻"),
                ("https://www.technologyreview.com/feed/", "MIT 科技评论"),
            },
            NegativeKeywords = new[] { "call for papers", "sponsored", "retracted", "case report", "editorial" },
        },

        new Topic
        {
            Id = "neuro",
            Name = "意识上传与神经科学",
            NameEn = "Mind Uploading & Neuroscience",
            Description = "脑机接口、神经解码、连接组、人工意识与神经形态计算",
            Color = "#B37FEB",
            Keywords = new[]
            {
                ("意识上传", Core), ("mind uploading", Core), ("mind upload", Core),
                ("脑机接口", Core), ("brain-computer interface", Core), ("BCI", Core),
                ("神经解码", Core), ("neural decoding", Core), ("脑机", Strong), ("neuroprosthetic", Strong),
                ("人工意识", Core), ("artificial consciousness", Core), ("machine consciousness", Core),
                ("连接组", Core), ("connectome", Core), ("connectomics", Strong),
                ("皮层", Strong), ("cortex", Strong), ("neural signal", Strong),
                ("神经信号", Strong), ("神经元", Strong), ("neuron", Strong),
                ("neural recording", Strong), ("神经记录", Strong), ("电信号", Support),
                ("记忆", Strong), ("memory consolidation", Strong), ("认知", Strong), ("cognition", Strong),
                ("神经形态", Strong), ("neuromorphic", Strong), ("认知科学", Support),
                ("意识", Strong), ("consciousness", Strong), ("意识科学", Strong),
                ("麻醉", Support), ("anesthesia", Support), ("脑机融合", Strong),
                ("neural implant", Strong), ("neural interface", Strong), ("mental", Support),
            },
            ArxivCategories = new[] { "q-bio.NC", "cs.NE", "cs.AI", "cs.LG", "cs.CL" },
            SearchQueries = new[]
            {
                "brain computer interface neural decoding",
                "connectome cortical neural network",
                "consciousness artificial intelligence neural",
                "neuromorphic computing spiking neural",
            },
            Feeds = new[]
            {
                ("https://www.sciencedaily.com/rss/mind_brain.xml", "ScienceDaily 脑与意识"),
                ("https://www.sciencedaily.com/rss/computers_math/neural_interfaces.xml", "ScienceDaily 神经接口"),
                ("https://www.sciencedaily.com/rss/health_medicine/nervous_system.xml", "ScienceDaily 神经系统"),
                ("https://phys.org/rss-feed/biology-news/neuroscience/", "Phys.org 神经科学"),
                ("https://www.quantamagazine.org/feed/", "Quanta Magazine"),
                ("https://spectrum.ieee.org/feeds/topic/artificial-intelligence.rss", "IEEE Spectrum AI"),
                ("https://www.technologyreview.com/feed/", "MIT 科技评论"),
            },
            NegativeKeywords = new[] { "call for papers", "sponsored" },
        },

        new Topic
        {
            Id = "space",
            Name = "太空探索与航天",
            NameEn = "Space Exploration",
            Description = "火箭与可重复使用发射、月球与火星基地、深空探测与系外行星",
            Color = "#F5C542",
            Keywords = new[]
            {
                ("火星", Core), ("mars", Core), ("月球", Core), ("moon", Core),
                ("太空探索", Core), ("space exploration", Core), ("火箭", Core), ("rocket", Core),
                ("可重复使用", Core), ("reusable launch", Core), ("星舰", Core), ("Starship", Core),
                ("星际", Core), ("interstellar", Core), ("系外行星", Core), ("exoplanet", Core),
                ("探月", Core), ("lunar", Strong), ("月球基地", Core), ("lunar base", Core),
                ("阿尔忒弥斯", Core), ("Artemis", Core), ("空间站", Strong), ("space station", Strong),
                ("探测器", Strong), ("spacecraft", Strong), ("探测器", Strong),
                ("小行星", Strong), ("asteroid", Strong), ("深空", Strong), ("deep space", Strong),
                ("轨道力学", Strong), ("orbital mechanics", Strong), ("推进", Support),
                ("核热推进", Strong), ("nuclear thermal", Strong), ("太空电梯", Strong),
                ("space elevator", Strong), ("詹姆斯韦伯", Strong), ("James Webb", Strong),
                ("詹姆斯·韦伯", Strong), ("望远镜", Support), ("telescope", Support),
                ("商业航天", Strong), ("commercial space", Strong), ("商业航天", Strong),
                ("星座", Support), ("satellite constellation", Strong), ("satellite", Support),
                ("星链", Strong), ("Starlink", Strong), ("发射", Support), ("launch vehicle", Strong),
                ("lander", Strong), ("rover", Strong), ("orbiter", Strong), ("regolith", Strong),
                ("astronaut", Strong), ("NASA", Strong), ("ESA", Strong), ("cosmic ray", Strong),
                ("payload", Support), ("booster", Support), ("thruster", Support), ("reentry", Support),
                ("propulsion", Support), ("probe", Support), ("mission control", Support),
            },
            ArxivCategories = new[] { "astro-ph", "physics.space-ph", "physics.ins-det" },
            SearchQueries = new[]
            {
                "lunar Mars exploration mission",
                "exoplanet atmosphere characterization",
                "spacecraft propulsion mission design",
                "satellite constellation space debris",
            },
            Feeds = new[]
            {
                ("https://science.nasa.gov/feed/", "NASA Science"),
                ("https://www.nasa.gov/rss/dyn/breaking_news.rss", "NASA 快讯"),
                ("https://www.nasa.gov/feed/", "NASA"),
                ("https://spacenews.com/feed/", "SpaceNews"),
                ("https://www.sciencedaily.com/rss/space_time.xml", "ScienceDaily 太空"),
                ("https://phys.org/rss-feed/space-news/", "Phys.org 太空"),
                ("https://www.livescience.com/feeds/all", "LiveScience"),
                ("https://feeds.arstechnica.com/arstechnica/index", "Ars Technica"),
            },
            NegativeKeywords = new[]
            {
                "call for papers", "horoscope", "astrology", "sponsored",
                "pokemon", "video game", "mobile game", "album", "soundtrack",
            },

            // "moon" / "mars" / "space" are far too generic on their own.
            AmbiguousKeywords = new[] { "moon", "mars", "space", "launch" },        },
    };

    public static Topic? Get(string id) => All.FirstOrDefault(t => t.Id == id);

    public static bool Exists(string id) => Get(id) is not null;
}
