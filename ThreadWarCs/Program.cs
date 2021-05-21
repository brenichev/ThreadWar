using PInvoke;
using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Collections.Generic;

namespace ThreadWarCs
{
    class Program
    {
        //HANDLE screenlock; // изменением экрана занимается только один поток
        Semaphore bulletsem;  // можно выстрелить только три раза подряд
        HANDLE startevt;     // игра начинается с нажатием клавиши "влево" или "вправо"
        //HANDLE conin, conout;  // дескрипторы консоли
        HANDLE mainthread; // Основной поток main
        CRITICAL_SECTION gameover;
        CONSOLE_SCREEN_BUFFER_INFO info; // информация о консоли
        static Mutex screenlock = new Mutex(false);
        static IntPtr conout, conin;
        object obj = new object();

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WriteConsoleOutputCharacter(
        IntPtr hConsoleOutput,
        [Out] StringBuilder lpCharacter,
        uint nLength,
        COORD dwReadCoord,
        out uint lpNumberOfCharsRead
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);
        const int STD_OUTPUT_HANDLE = -11;

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ReadConsoleOutputCharacter(
        IntPtr hConsoleOutput,
        [Out] StringBuilder lpCharacter,
        uint nLength,
        COORD dwReadCoord,
        out uint lpNumberOfCharsRead
        );

        // количество попаданий и промахов
        long hit = 0;
        long miss = 0;
        char[] badchar = "-\\|/".ToCharArray();

        // Создание случайного числа от n0 до n1
        int random(int n0, int n1)
        {
            if (n0 == 0 && n1 == 1) return rand() % 2;
            return rand() % (n1 - n0) + n0;
        }

        // вывести на экран символ в позицию х и у 

        void writeat(short x, short y, StringBuilder c)
        {
            // Блокировать вывод на экран при помощи мьютекса
            screenlock.WaitOne();
            COORD pos;
            pos.X = x;
            pos.Y = y;
            //uint res = 1; //&&&&&&&&&
            WriteConsoleOutputCharacter(conout, c, 1, pos, out uint res);
            screenlock.ReleaseMutex();
        }

        // Получить нажатие на клавишу (счетчик повторейний в ct) 
        int getakey(int& ct)
        {
            INPUT_RECORD input;
            DWORD res;
            while (1)
            {
                ReadConsoleInput(conin, &input, 1, &res);
                // игнорировать другие события
                if (input.EventType != KEY_EVENT) continue;
                // игнорировать события отпускания клавиш 
                // нас интересуют только нажатия
                if (!input.Event.KeyEvent.bKeyDown) continue;
                ct = input.Event.KeyEvent.wRepeatCount;
                return input.Event.KeyEvent.wVirtualKeyCode;
            }
        }

        // Определить символ в заданной позиции экрана
        int getat(short x, short y)
        {
            StringBuilder c = new StringBuilder();            
            COORD org;
            org.X = x;
            org.Y = y;
            // Блокировать доступ к консоли до тех пор, пока процедура не будет выполнена
            screenlock.WaitOne();
            ReadConsoleOutputCharacter(conout, c, 1, org, out uint res);
            screenlock.ReleaseMutex(); // unlock
            return c[0];
        }

        // Отобразить очки в заголовке окна и проверить условие завершения игры 
        void score()
        {
            //wchar_t s[128];
            Console.Title = $"Война потоков - Попаданий:{hit}, Промахов:{miss}";
            if (miss >= 30)
            {
                lock(obj)
                {
                    //SuspendThread(mainthread); // stop main thread
                    MessageBox.Show("Игра окончена!", "Thread War", MessageBoxButton.OK);
                    Environment.Exit(0); // не выходит из критической секции
                }
            }
        }

        // это поток противника 
        void badguy()
        {
            short y = (short)rnd.Next(1, 15); // случайная координата у
            int dir;
            short x;
            // нечетные у появляются слева, четные у появляются справа
            x = y % 2 ? 0 : info.dwSize.X;
            // установить направление в зависимости от начальной позиции
            dir = x ? -1 : 1;
            // пока противник находится в пределах экрана
            while ((dir == 1 && x != info.dwSize.X) || (dir == -1 && x != 0))

            {
                int dly;
                bool hitme = false;
                writeat(x, y, badchar[x % 4].ToString());

                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(40);
                    if (getat(x, y) == '*')
                    {
                        hitme = true;
                        break;
                    }
                }
                writeat(x, y, " ");

                if (hitme)
                {
                    // в противника попали!
                    MessageBeep(-1);
                    Interlocked.Increment(ref hit);
                    score();
                    //_endthread();
                    return;
                }
                x += (short)dir;
            }
            // противник убежал!
            Interlocked.Increment(ref miss);
            score();
        }

        // этот поток занимается созданием потоков противников
        void badguys()
        {
            // ждем сигнала к началу игры в течение 15 секунд
            WaitForSingleObject(startevt, 15000);
            // создаем случайного врага
            // каждые 5 секунд появляется шанс создать
            // противника с координатами от 1 до 10
            while (1)
            {
                if (random(0, 100) < (hit + miss) / 25 + 20)
                    // со временем вероятность увеличивается
                    _beginthread(badguy, 0, (void*)(random(1, 10)));
                Sleep(1000); // каждую секунду
            }
        }

        // Это поток пули, каждая пуля - это отдельный поток
        void bullet(object obj)
        {
            COORD xy = (COORD)obj;
            if (getat(xy.X, xy.Y) == '*') return; // здесь уже есть пуля
                                                  // надо подождать 
                                                  // Проверить семафор
                                                  // если семафор равен 0, выстрела не происходит 
            if (WaitForSingleObject(bulletsem, 0) == WAIT_TIMEOUT) return;

            while (--xy.Y != 1)
            {
                writeat(xy.X, xy.Y, '*'); // отобразить пулю
                Thread.Sleep(100);
                writeat(xy.X, xy.Y, ' ');    // стереть пулю
            }

            // выстрел сделан.- добавить 1 к семафору
            ReleaseSemaphore(bulletsem, 1, NULL);
        }

        // Основная программа
        static void Main(string[] args)
        {
            //HANDLE me;
            // Настройка глобальных переменных
            conin = GetStdHandle(STD_INPUT_HANDLE);
            conout = GetStdHandle(STD_OUTPUT_HANDLE);
            SetConsoleMode(conin, ENABLE_WINDOW_INPUT);
            me = GetCurrentThread(); // не является реальным дескриптором
                                     // изменить псевдодескриптор на реальный дескриптор текущего потока
            DuplicateHandle(GetCurrentProcess(), me, GetCurrentProcess(), &mainthread, 0, FALSE, DUPLICATE_SAME_ACCESS);
            startevt = CreateEvent(NULL, TRUE, FALSE, NULL);
            screenlock = CreateMutex(NULL, FALSE, NULL);
            InitializeCriticalSection(&gameover);
            bulletsem = CreateSemaphore(NULL, 3, 3, NULL);
            GetConsoleScreenBufferInfo(conout, &info);

            // Инициализировать отображение информации об очках
            score();
            // Настроить генератор случайных чисел
            srand((unsigned)time(NULL));

            // установка начальной позиции пушки
            int y = info.dwSize.Y - 1;
            int x = info.dwSize.X / 2;
            // запустить поток badguys; ничего не делать до тех пор, 
            // пока не произойдет событие или истекут 15 секунд
            _beginthread(badguys, 0, NULL); // основной цикл игры
            while (true)
            {
                int c, ct;
                writeat(x, y, '|'); // нарисовать пушку 
                c = getakey(ct);   // получить символ
                switch (c)
                {
                    case VK_SPACE:
                        static COORD xy;
                        xy.X = x;
                        xy.Y = y;
                        _beginthread(bullet, 0, (void*)&xy);
                        Sleep(100); // дать пуле время улететь на некоторое расстояние
                        break;
                    case VK_LEFT:  // команда "влево!"
                        SetEvent(startevt);    // поток badguys работает 
                        writeat(x, y, ' ');      // убрать с экрана пушку 
                        while (ct--)        // переместиться
                            if (x) x--;
                        break;
                    case VK_RIGHT: // команда "вправо!"; логика та же
                        SetEvent(startevt);
                        writeat(x, y, ' ');
                        while (ct--)
                            if (x != info.dwSize.X - 1) x++;
                        break;
                }
            }
        }
    }
}
