function [filtercoefficients]=CoefficientsGenerator(Fs,order, filtertype,Fc1,Fc2)
% function [filtercoefficients]=CoefficientsGenerator[Fs,order, Fc1,Fc2,filtertype]
% Fs 采样率
% order 滤波器阶数
% filtertype，可选low,high,bandpass三种
% Fc1,Fc2 截止频率，低通以及高通可以缺省
% filtercoefficients 输出滤波器系数
if nargin < 5
    Fc2=[];
end
flag = 'scale';
win = blackman(order+1);
if(strcmp(filtertype(1),'l'))
    filtercoefficients=fir1(order, Fc1/(Fs/2), 'low', win, flag);
end
if(strcmp(filtertype(1),'h'))
    filtercoefficients=fir1(order, Fc1/(Fs/2), 'high', win, flag,'h');
end
if(strcmp(filtertype(1),'b'))
    filtercoefficients=fir1(order, [Fc1 Fc2]/(Fs/2), 'bandpass', win, flag);
end
end